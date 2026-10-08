using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using Ymir.VibeMaker.Application.Knowledge;

namespace Ymir.VibeMaker.Infrastructure.Knowledge;

/// <summary>
/// 每個專案一份 SQLite 的向量儲存（ADR-0014 §4）：段落文字與 float32 向量存在 <c>chunks</c>，
/// 搜尋時只讀指定文件版本的向量做餘弦相似度暴力搜尋。寫入由 <see cref="KnowledgeLocks"/> 保證每專案單一 writer。
/// </summary>
internal sealed class SqliteVectorStore(KnowledgePaths paths) : IVectorStore
{
    private const string Schema = """
        PRAGMA journal_mode=WAL;
        CREATE TABLE IF NOT EXISTS chunks (
          id INTEGER PRIMARY KEY,
          document_id TEXT NOT NULL,
          ordinal INTEGER NOT NULL,
          page INTEGER NULL,
          text TEXT NOT NULL,
          vector BLOB NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_chunks_document ON chunks(document_id);
        """;

    public async Task WriteAsync(Guid userId, Guid projectId, Guid documentId, IReadOnlyList<TextChunk> chunks, IReadOnlyList<float[]> vectors, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(vectors);
        var connection = await OpenAsync(userId, projectId, create: true, cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await DeleteRowsAsync(connection, transaction, documentId, cancellationToken).ConfigureAwait(false);
                var insert = connection.CreateCommand();
                await using (insert.ConfigureAwait(false))
                {
                    insert.Transaction = transaction;
                    insert.CommandText = "INSERT INTO chunks(document_id, ordinal, page, text, vector) VALUES ($document, $ordinal, $page, $text, $vector)";
                    var document = insert.Parameters.Add("$document", SqliteType.Text);
                    var ordinal = insert.Parameters.Add("$ordinal", SqliteType.Integer);
                    var page = insert.Parameters.Add("$page", SqliteType.Integer);
                    var text = insert.Parameters.Add("$text", SqliteType.Text);
                    var vector = insert.Parameters.Add("$vector", SqliteType.Blob);
                    document.Value = documentId.ToString("N");
                    for (var i = 0; i < chunks.Count; i++)
                    {
                        ordinal.Value = chunks[i].Ordinal;
                        page.Value = chunks[i].Page is { } p ? p : DBNull.Value;
                        text.Value = chunks[i].Text;
                        vector.Value = MemoryMarshal.AsBytes(vectors[i].AsSpan()).ToArray();
                        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task DeleteAsync(Guid userId, Guid projectId, Guid documentId, CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.IndexPath(userId, projectId)))
        {
            return;
        }

        var connection = await OpenAsync(userId, projectId, create: false, cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await DeleteRowsAsync(connection, null, documentId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<VectorHit>> SearchAsync(Guid userId, Guid projectId, IReadOnlyCollection<Guid> documentIds, float[] query, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        ArgumentNullException.ThrowIfNull(query);
        if (documentIds.Count == 0 || !File.Exists(paths.IndexPath(userId, projectId)))
        {
            return [];
        }

        var allowed = documentIds.ToDictionary(id => id.ToString("N"));
        var hits = new List<VectorHit>();
        var connection = await OpenAsync(userId, projectId, create: false, cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var select = connection.CreateCommand();
            await using (select.ConfigureAwait(false))
            {
                select.CommandText = "SELECT document_id, ordinal, page, text, vector FROM chunks";
                var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        // 只在呼叫端給的有效版本中搜尋（專案邊界與版本切換，ADR-0014 §4、§7）。
                        if (!allowed.TryGetValue(reader.GetString(0), out var documentId))
                        {
                            continue;
                        }

                        var vector = MemoryMarshal.Cast<byte, float>((byte[])reader.GetValue(4));
                        if (vector.Length != query.Length)
                        {
                            continue;
                        }

                        hits.Add(new VectorHit(documentId, reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetInt32(2), reader.GetString(3), Cosine(query, vector)));
                    }
                }
            }
        }

        return [.. hits.OrderByDescending(h => h.Score).Take(limit)];
    }

    internal static double Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return normA == 0 || normB == 0 ? 0 : dot / Math.Sqrt(normA * normB);
    }

    private async Task<SqliteConnection> OpenAsync(Guid userId, Guid projectId, bool create, CancellationToken cancellationToken)
    {
        var path = paths.IndexPath(userId, projectId);
        if (create)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        }

        // 不使用連線池：檔案不會在背景一直被開著（備份、移除專案資料時比較單純）。
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            Pooling = false,
            DefaultTimeout = 30,
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var schema = connection.CreateCommand();
        await using (schema.ConfigureAwait(false))
        {
            schema.CommandText = Schema;
            await schema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return connection;
    }

    private static async Task DeleteRowsAsync(SqliteConnection connection, SqliteTransaction? transaction, Guid documentId, CancellationToken cancellationToken)
    {
        var delete = connection.CreateCommand();
        await using (delete.ConfigureAwait(false))
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM chunks WHERE document_id = $document";
            delete.Parameters.AddWithValue("$document", documentId.ToString("N"));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
