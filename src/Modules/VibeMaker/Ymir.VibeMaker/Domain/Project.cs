namespace Ymir.VibeMaker.Domain;

/// <summary>
/// 專案：使用者 runtime 內的一個檔案群組（ADR-0007，取代 SA §8 WORKSPACE）。
/// 檔案位於 container 的 <c>/workspace/projects/{id}</c>，同一專案的對話共用這些檔案；執行環境屬於使用者，不屬於專案。
/// </summary>
public sealed class Project
{
    public const int NameMaxLength = 200;

    private Project()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public ProjectStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Project Create(Guid userId, string name, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            Name = DomainGuard.RequiredText(name, NameMaxLength, nameof(name)),
            Status = ProjectStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };

    public void Touch(DateTimeOffset now) => UpdatedAt = now;
}

public enum ProjectStatus
{
    Active = 0,
    Archived = 1,
}
