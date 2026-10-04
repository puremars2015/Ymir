namespace Ymir.VibeMaker.Domain;

/// <summary>使用者的工作空間（SA §8 WORKSPACE）。一個 Active Workspace 對應一個 Agent runtime（SA §6.1）。</summary>
public sealed class Workspace
{
    public const int NameMaxLength = 200;

    private Workspace()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>邏輯儲存位置（目前等於 workspace id）；host 路徑只由 Runtime Manager 解析（SA §12）。</summary>
    public string StorageKey { get; private set; } = string.Empty;

    public WorkspaceStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Workspace Create(Guid userId, string name, DateTimeOffset now)
    {
        var id = Guid.CreateVersion7(now);
        return new Workspace
        {
            Id = id,
            UserId = userId,
            Name = DomainGuard.RequiredText(name, NameMaxLength, nameof(name)),
            StorageKey = id.ToString("N"),
            Status = WorkspaceStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}

public enum WorkspaceStatus
{
    Active = 0,
    Archived = 1,
}
