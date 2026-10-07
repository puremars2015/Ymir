namespace Ymir.VibeMaker.Domain;

public enum KnowledgeDocumentStatus
{
    /// <summary>等待背景索引。</summary>
    Pending,

    Indexing,

    /// <summary>可查詢（同專案同檔名最多一份，由 filtered unique index 保證）。</summary>
    Ready,

    Failed,

    /// <summary>已移除或被新版本取代；段落與原始檔已刪除，不再被查詢。</summary>
    Removed,
}

/// <summary>
/// 專案知識庫中的一份文件版本（ADR-0014）。每次上傳是一筆新的版本；新版本索引成功後，同檔名的舊版本改為 <see cref="KnowledgeDocumentStatus.Removed"/>。
/// 原始檔與段落放在 API 自己的知識庫 volume（由 id 推導路徑）；這裡只存管理資料。跨模組只存 user id（ADR-0001）。
/// </summary>
public sealed class KnowledgeDocument
{
    public const int FileNameMaxLength = 255;
    public const int ErrorMaxLength = 300;

    private KnowledgeDocument()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid ProjectId { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public long Size { get; private set; }

    /// <summary>原始檔的 SHA-256（hex）。</summary>
    public string ContentHash { get; private set; } = string.Empty;

    /// <summary>同專案同檔名的第幾個版本（從 1 開始）。</summary>
    public int Version { get; private set; }

    public KnowledgeDocumentStatus Status { get; private set; }

    public int ChunkCount { get; private set; }

    /// <summary>建立索引時的 Embedding 模型與維度；查詢時只使用相同設定的版本（ADR-0014 §3）。</summary>
    public string? EmbeddingModel { get; private set; }

    public int? Dimensions { get; private set; }

    public string? Error { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static KnowledgeDocument Create(Guid id, Guid userId, Guid projectId, string fileName, long size, string contentHash, int version, DateTimeOffset now) =>
        new()
        {
            Id = id,
            UserId = userId,
            ProjectId = projectId,
            FileName = DomainGuard.RequiredText(fileName, FileNameMaxLength, nameof(fileName)),
            Size = size,
            ContentHash = DomainGuard.RequiredText(contentHash, 64, nameof(contentHash)),
            Version = version,
            Status = KnowledgeDocumentStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };

    public void StartIndexing(DateTimeOffset now)
    {
        Status = KnowledgeDocumentStatus.Indexing;
        Error = null;
        UpdatedAt = now;
    }

    public void MarkReady(int chunkCount, string embeddingModel, int dimensions, DateTimeOffset now)
    {
        Status = KnowledgeDocumentStatus.Ready;
        ChunkCount = chunkCount;
        EmbeddingModel = embeddingModel;
        Dimensions = dimensions;
        Error = null;
        UpdatedAt = now;
    }

    public void MarkFailed(string error, DateTimeOffset now)
    {
        Status = KnowledgeDocumentStatus.Failed;
        Error = error.Length > ErrorMaxLength ? error[..ErrorMaxLength] : error;
        UpdatedAt = now;
    }

    /// <summary>重試或重啟後重做：回到等待索引。</summary>
    public void Requeue(DateTimeOffset now)
    {
        Status = KnowledgeDocumentStatus.Pending;
        Error = null;
        UpdatedAt = now;
    }

    public void Remove(DateTimeOffset now)
    {
        Status = KnowledgeDocumentStatus.Removed;
        UpdatedAt = now;
    }
}
