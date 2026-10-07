using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ymir.Platform.Auditing;
using Ymir.VibeMaker.Application.Models;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Knowledge;

/// <summary>每個專案同時只有一個寫入者（SQLite 單一 writer，ADR-0014 §2）。</summary>
public sealed class KnowledgeLocks
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var semaphore = _locks.GetOrAdd(projectId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}

public enum KnowledgeUploadError
{
    None,
    Disabled,
    UnsupportedType,
    TooLarge,
    TooManyDocuments,
}

/// <summary>
/// 專案知識庫（ADR-0014）：上傳文件、背景索引（擷取 → 切段 → embedding → 寫入向量）、移除與重試。
/// 所有操作都以呼叫端給的 user id 驗證專案擁有者（SA §12）；專案不存在或不是自己的回傳 null（→ 404）。
/// </summary>
public sealed partial class KnowledgeBaseService(
    IVibeMakerDbContext db,
    IKnowledgeFileStore files,
    IDocumentTextExtractor extractor,
    IEmbeddingClient embeddings,
    IVectorStore vectors,
    RuntimeCredentialService credentials,
    KnowledgeSignal signal,
    KnowledgeLocks locks,
    IOptions<KnowledgeOptions> options,
    IAuditLog auditLog,
    TimeProvider timeProvider,
    ILogger<KnowledgeBaseService> logger)
{
    public static readonly IReadOnlyList<string> SupportedExtensions = [".txt", ".md", ".markdown", ".pdf", ".docx"];

    private readonly KnowledgeOptions _options = options.Value;

    public bool IsEnabled => _options.IsEnabled;

    public KnowledgeOptions Options => _options;

    /// <summary>專案的文件（不含已移除）；專案不存在或不是自己的時回傳 null。</summary>
    public async Task<IReadOnlyList<KnowledgeDocument>?> ListAsync(Guid userId, Guid projectId, CancellationToken cancellationToken)
    {
        if (!await OwnsProjectAsync(userId, projectId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await db.KnowledgeDocuments.AsNoTracking()
            .Where(d => d.ProjectId == projectId && d.UserId == userId && d.Status != KnowledgeDocumentStatus.Removed)
            .OrderBy(d => d.FileName).ThenByDescending(d => d.Version)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <returns>專案不存在時為 null；否則是新文件或錯誤原因。</returns>
    public async Task<(KnowledgeDocument? Document, KnowledgeUploadError Error)?> UploadAsync(
        Guid userId,
        Guid projectId,
        string? fileName,
        Stream content,
        string actor,
        CancellationToken cancellationToken)
    {
        if (!await OwnsProjectAsync(userId, projectId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        if (!IsEnabled)
        {
            return (null, KnowledgeUploadError.Disabled);
        }

        var name = SafeFileName(fileName);
        if (name is null || !SupportedExtensions.Contains(Path.GetExtension(name).ToLowerInvariant()))
        {
            return (null, KnowledgeUploadError.UnsupportedType);
        }

        var names = await db.KnowledgeDocuments
            .Where(d => d.ProjectId == projectId && d.Status != KnowledgeDocumentStatus.Removed)
            .Select(d => d.FileName).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
        if (!names.Contains(name, StringComparer.Ordinal) && names.Count >= _options.MaxDocuments)
        {
            return (null, KnowledgeUploadError.TooManyDocuments);
        }

        var id = Guid.CreateVersion7();
        var saved = await files.SaveAsync(userId, projectId, id, content, _options.MaxFileBytes, cancellationToken).ConfigureAwait(false);
        if (saved is not { } file)
        {
            return (null, KnowledgeUploadError.TooLarge);
        }

        var version = (await db.KnowledgeDocuments.Where(d => d.ProjectId == projectId && d.FileName == name)
            .MaxAsync(d => (int?)d.Version, cancellationToken).ConfigureAwait(false) ?? 0) + 1;
        var now = timeProvider.GetUtcNow();
        var document = KnowledgeDocument.Create(id, userId, projectId, name, file.Size, file.Sha256, version, now);
        db.KnowledgeDocuments.Add(document);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await AuditAsync(actor, "knowledge.document.upload", id, cancellationToken).ConfigureAwait(false);
        signal.Notify();
        return (document, KnowledgeUploadError.None);
    }

    /// <returns>找不到（或不是自己的）文件時回傳 false。</returns>
    public async Task<bool> RemoveAsync(Guid userId, Guid projectId, Guid documentId, string actor, CancellationToken cancellationToken)
    {
        if (!await OwnsProjectAsync(userId, projectId, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        using var projectLock = await locks.AcquireAsync(projectId, cancellationToken).ConfigureAwait(false);
        var document = await FindAsync(userId, projectId, documentId, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return false;
        }

        // 先刪除段落再標記移除：之後的查詢一定不會再引用（ADR-0014 §7）。
        await vectors.DeleteAsync(userId, projectId, documentId, cancellationToken).ConfigureAwait(false);
        files.Delete(userId, projectId, documentId);
        document.Remove(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await AuditAsync(actor, "knowledge.document.remove", documentId, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <returns>找不到文件時回傳 null；不是失敗狀態時回傳文件本身（不變）。</returns>
    public async Task<KnowledgeDocument?> RetryAsync(Guid userId, Guid projectId, Guid documentId, CancellationToken cancellationToken)
    {
        if (!await OwnsProjectAsync(userId, projectId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var document = await FindAsync(userId, projectId, documentId, cancellationToken).ConfigureAwait(false);
        if (document is { Status: KnowledgeDocumentStatus.Failed })
        {
            document.Requeue(timeProvider.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            signal.Notify();
        }

        return document;
    }

    /// <summary>啟動時對帳：中斷的索引改回等待（重做時會先刪除已寫入的段落，不會重複）。</summary>
    public async Task<int> ReconcileAsync(CancellationToken cancellationToken)
    {
        var interrupted = await db.KnowledgeDocuments.Where(d => d.Status == KnowledgeDocumentStatus.Indexing).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var document in interrupted)
        {
            document.Requeue(timeProvider.GetUtcNow());
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return interrupted.Count;
    }

    /// <summary>處理一份等待索引的文件（worker 用）；沒有工作時回傳 false。</summary>
    public async Task<bool> IndexNextAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return false;
        }

        var next = await db.KnowledgeDocuments.AsNoTracking()
            .Where(d => d.Status == KnowledgeDocumentStatus.Pending)
            .OrderBy(d => d.CreatedAt)
            .Select(d => new { d.Id, d.ProjectId })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (next is null)
        {
            return false;
        }

        using var projectLock = await locks.AcquireAsync(next.ProjectId, cancellationToken).ConfigureAwait(false);
        var document = await db.KnowledgeDocuments.SingleAsync(d => d.Id == next.Id, cancellationToken).ConfigureAwait(false);
        if (document.Status != KnowledgeDocumentStatus.Pending)
        {
            return true;
        }

        document.StartIndexing(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await IndexAsync(document, cancellationToken).ConfigureAwait(false);
        }
        catch (KnowledgeException ex)
        {
            await FailAsync(document, ex.Message, ex).ConfigureAwait(false);
        }
        catch (ModelCredentialException ex)
        {
            await FailAsync(document, "暫時無法連線到模型服務，請稍後重試。", ex).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // 單一文件索引失敗不得讓 worker 停止；原始例外只寫 server log。
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            await FailAsync(document, "建立索引失敗，請稍後重試。", ex).ConfigureAwait(false);
        }

        return true;
    }

    private async Task IndexAsync(KnowledgeDocument document, CancellationToken cancellationToken)
    {
        IReadOnlyList<(int? Page, string Text)> pages;
        var stream = files.OpenRead(document.UserId, document.ProjectId, document.Id);
        await using (stream.ConfigureAwait(false))
        {
            pages = extractor.Extract(document.FileName, stream);
        }

        var chunks = TextChunker.Chunk(pages, _options.ChunkSize, _options.ChunkOverlap);
        if (chunks.Count == 0)
        {
            throw new KnowledgeException("文件沒有可擷取的文字（掃描檔或圖片需要 OCR，目前不支援）。");
        }

        var existing = await db.KnowledgeDocuments
            .Where(d => d.ProjectId == document.ProjectId && d.Status == KnowledgeDocumentStatus.Ready && d.FileName != document.FileName)
            .SumAsync(d => d.ChunkCount, cancellationToken).ConfigureAwait(false);
        if (existing + chunks.Count > _options.MaxChunks)
        {
            throw new KnowledgeException($"專案知識庫的段落超過上限（{_options.MaxChunks} 段），請移除部分文件。");
        }

        var credential = await credentials.GetAsync(document.UserId, Guid.Empty, cancellationToken).ConfigureAwait(false);
        var model = _options.EmbeddingModel!;
        var vectorsOut = new List<float[]>(chunks.Count);
        foreach (var batch in chunks.Chunk(Math.Max(1, _options.EmbeddingBatchSize)))
        {
            vectorsOut.AddRange(await embeddings.EmbedAsync(credential.ApiKey, model, [.. batch.Select(c => c.Text)], cancellationToken).ConfigureAwait(false));
        }

        if (vectorsOut.Count != chunks.Count || vectorsOut.Select(v => v.Length).Distinct().Count() != 1)
        {
            throw new KnowledgeException("Embedding 服務回傳的向量不完整，請稍後重試。");
        }

        await vectors.WriteAsync(document.UserId, document.ProjectId, document.Id, chunks, vectorsOut, cancellationToken).ConfigureAwait(false);

        // 期間被移除：清掉剛寫入的段落。
        await db.Entry(document).ReloadAsync(cancellationToken).ConfigureAwait(false);
        if (document.Status != KnowledgeDocumentStatus.Indexing)
        {
            await vectors.DeleteAsync(document.UserId, document.ProjectId, document.Id, cancellationToken).ConfigureAwait(false);
            return;
        }

        // 新版本完整寫入後才切換：先移除同檔名的舊版本（filtered unique index 只允許一份 Ready），再標記新版本可查詢。
        var now = timeProvider.GetUtcNow();
        var previous = await db.KnowledgeDocuments
            .Where(d => d.ProjectId == document.ProjectId && d.FileName == document.FileName && d.Id != document.Id
                && (d.Status == KnowledgeDocumentStatus.Ready || d.Status == KnowledgeDocumentStatus.Failed))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var old in previous)
        {
            await vectors.DeleteAsync(old.UserId, old.ProjectId, old.Id, cancellationToken).ConfigureAwait(false);
            files.Delete(old.UserId, old.ProjectId, old.Id);
            old.Remove(now);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        document.MarkReady(chunks.Count, model, vectorsOut[0].Length, now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task FailAsync(KnowledgeDocument document, string summary, Exception exception)
    {
        LogIndexFailed(logger, exception, document.Id);
        await vectors.DeleteAsync(document.UserId, document.ProjectId, document.Id, CancellationToken.None).ConfigureAwait(false);
        await db.Entry(document).ReloadAsync(CancellationToken.None).ConfigureAwait(false);
        if (document.Status == KnowledgeDocumentStatus.Indexing)
        {
            document.MarkFailed(summary, timeProvider.GetUtcNow());
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private Task<bool> OwnsProjectAsync(Guid userId, Guid projectId, CancellationToken cancellationToken) =>
        db.Projects.AnyAsync(p => p.Id == projectId && p.UserId == userId && p.Status == ProjectStatus.Active, cancellationToken);

    private Task<KnowledgeDocument?> FindAsync(Guid userId, Guid projectId, Guid documentId, CancellationToken cancellationToken) =>
        db.KnowledgeDocuments.SingleOrDefaultAsync(
            d => d.Id == documentId && d.ProjectId == projectId && d.UserId == userId && d.Status != KnowledgeDocumentStatus.Removed,
            cancellationToken);

    /// <summary>只取檔名（去掉任何路徑），拒絕控制字元與空白檔名。</summary>
    internal static string? SafeFileName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/')).Trim();
        return name.Length is 0 or > KnowledgeDocument.FileNameMaxLength || name.Any(char.IsControl) || name.StartsWith('.') ? null : name;
    }

    private Task AuditAsync(string actor, string action, Guid documentId, CancellationToken cancellationToken) =>
        auditLog.WriteAsync(new AuditEntry(actor, action, "knowledge-document", documentId.ToString("D"), AuditResult.Success, timeProvider.GetUtcNow(), null), cancellationToken);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Indexing knowledge document {DocumentId} failed")]
    private static partial void LogIndexFailed(ILogger logger, Exception exception, Guid documentId);
}
