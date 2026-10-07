namespace Ymir.VibeMaker.Domain;

/// <summary>成功執行明確交付的檔案；內容由使用者 runtime 持久化。</summary>
public sealed class ExecutionArtifact
{
    private ExecutionArtifact() { }
    public Guid Id { get; private set; }
    public Guid ExecutionId { get; private set; }
    public string Path { get; private set; } = string.Empty;
    public long Size { get; private set; }
    public DateTimeOffset ModifiedAt { get; private set; }

    public static ExecutionArtifact Create(Guid executionId, string path, long size, DateTimeOffset modifiedAt) => new()
    {
        Id = Guid.CreateVersion7(),
        ExecutionId = executionId,
        Path = DomainGuard.RequiredText(path, 1024, nameof(path)),
        Size = size >= 0 ? size : throw new DomainValidationException("Invalid artifact size."),
        ModifiedAt = modifiedAt,
    };
}
