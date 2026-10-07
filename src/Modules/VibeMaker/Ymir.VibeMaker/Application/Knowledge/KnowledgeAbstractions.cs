namespace Ymir.VibeMaker.Application.Knowledge;

/// <summary><c>VibeMaker:Rag</c>（ADR-0014 §3、§6）。沒有設定 <see cref="EmbeddingModel"/> 時知識庫停用。</summary>
public sealed class KnowledgeOptions
{
    public const string SectionName = "VibeMaker:Rag";

    /// <summary>LiteLLM 的 embedding 模型名稱（model_name）。</summary>
    public string? EmbeddingModel { get; set; }

    /// <summary>API 呼叫 LiteLLM 的位址；未設定時依序使用 <c>VibeMaker:LiteLlm:BaseUrl</c>、<c>VibeMaker:Pi:ModelBaseUrl</c>。</summary>
    public Uri? BaseUrl { get; set; }

    public long MaxFileBytes { get; set; } = 20L * 1024 * 1024;

    public int MaxDocuments { get; set; } = 200;

    public int MaxChunks { get; set; } = 20_000;

    public int ChunkSize { get; set; } = 800;

    public int ChunkOverlap { get; set; } = 100;

    public int EmbeddingBatchSize { get; set; } = 32;

    public int TopK { get; set; } = 6;

    /// <summary>最高相似度低於此值時回「資料不足」，不呼叫回答模型（ADR-0014 §8）。</summary>
    public double MinScore { get; set; } = 0.2;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    public bool IsEnabled => !string.IsNullOrWhiteSpace(EmbeddingModel);
}

/// <summary>經 LiteLLM 的 embedding（ADR-0014 §3）；使用該使用者的 virtual key。</summary>
public interface IEmbeddingClient
{
    /// <summary>依輸入順序回傳向量；失敗時拋出 <see cref="KnowledgeException"/>（訊息是給使用者看的摘要）。</summary>
    Task<IReadOnlyList<float[]>> EmbedAsync(string apiKey, string model, IReadOnlyList<string> inputs, CancellationToken cancellationToken);
}

/// <param name="Page">PDF 的頁碼；其他格式為 null。</param>
public sealed record TextChunk(int Ordinal, int? Page, string Text);

public sealed record VectorHit(Guid DocumentId, int Ordinal, int? Page, string Text, double Score);

/// <summary>每個專案一份的向量儲存（ADR-0014 §4）。專案邊界由路徑（user id + project id）與 document id 共同保證。</summary>
public interface IVectorStore
{
    /// <summary>寫入一個文件版本的段落（同一 document id 已有段落時先刪除，避免重做時重複）。</summary>
    Task WriteAsync(Guid userId, Guid projectId, Guid documentId, IReadOnlyList<TextChunk> chunks, IReadOnlyList<float[]> vectors, CancellationToken cancellationToken);

    Task DeleteAsync(Guid userId, Guid projectId, Guid documentId, CancellationToken cancellationToken);

    /// <summary>只在 <paramref name="documentIds"/>（該專案的有效版本）中搜尋前 <paramref name="limit"/> 段。</summary>
    Task<IReadOnlyList<VectorHit>> SearchAsync(Guid userId, Guid projectId, IReadOnlyCollection<Guid> documentIds, float[] query, int limit, CancellationToken cancellationToken);
}

/// <summary>原始文件的保存（API 自己的知識庫 volume，路徑只由 id 推導）。</summary>
public interface IKnowledgeFileStore
{
    /// <summary>串流寫入並計算大小與 SHA-256；超過 <paramref name="maxBytes"/> 時刪除並回傳 null。</summary>
    Task<(long Size, string Sha256)?> SaveAsync(Guid userId, Guid projectId, Guid documentId, Stream content, long maxBytes, CancellationToken cancellationToken);

    Stream OpenRead(Guid userId, Guid projectId, Guid documentId);

    void Delete(Guid userId, Guid projectId, Guid documentId);
}

/// <summary>擷取文件文字（ADR-0014 §5）。不支援或沒有文字時拋出 <see cref="KnowledgeException"/>。</summary>
public interface IDocumentTextExtractor
{
    IReadOnlyList<(int? Page, string Text)> Extract(string fileName, Stream content);
}

/// <summary>知識庫流程的錯誤；<see cref="Exception.Message"/> 是可以給使用者看的摘要。</summary>
public sealed class KnowledgeException(string summary, Exception? inner = null) : Exception(summary, inner);

/// <summary>喚醒索引 worker。</summary>
public sealed class KnowledgeSignal : IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // 已經有一個未處理的通知。
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _signal.WaitAsync(timeout, cancellationToken);

    public void Dispose() => _signal.Dispose();
}
