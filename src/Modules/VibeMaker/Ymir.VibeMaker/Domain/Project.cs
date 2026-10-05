namespace Ymir.VibeMaker.Domain;

/// <summary>
/// 專案：使用者 runtime 內的一個檔案群組（ADR-0007，取代 SA §8 WORKSPACE）。
/// 檔案位於 container 的 <c>/workspace/projects/{id}</c>，同一專案的對話共用這些檔案；執行環境屬於使用者，不屬於專案。
/// </summary>
public sealed class Project
{
    public const int NameMaxLength = 200;

    /// <summary>system prompt 上限（個人與專案相同），避免過長的 prompt 吃掉模型的 context。</summary>
    public const int SystemPromptMaxLength = 10_000;

    private Project()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>專案專用的 system prompt，附加在個人 system prompt 之後（Pi 的預設 prompt 保留）。</summary>
    public string? SystemPrompt { get; private set; }

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

    public void Rename(string name, DateTimeOffset now)
    {
        Name = DomainGuard.RequiredText(name, NameMaxLength, nameof(name));
        UpdatedAt = now;
    }

    public void SetSystemPrompt(string? systemPrompt, DateTimeOffset now)
    {
        SystemPrompt = DomainGuard.OptionalText(systemPrompt, SystemPromptMaxLength, "systemPrompt");
        UpdatedAt = now;
    }
}

public enum ProjectStatus
{
    Active = 0,
    Archived = 1,
}
