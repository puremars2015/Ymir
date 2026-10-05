namespace Ymir.VibeMaker.Domain;

/// <summary>
/// 使用者在 Vibe Maker 的個人設定。跨模組只存 user id、不建 FK（ADR-0001）；
/// 尚未設定過的使用者沒有資料列。
/// </summary>
public sealed class UserSettings
{
    private UserSettings()
    {
    }

    public Guid UserId { get; private set; }

    /// <summary>個人 global system prompt，套用到這位使用者的所有對話（Pi 的預設 prompt 保留）。</summary>
    public string? SystemPrompt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static UserSettings Create(Guid userId, DateTimeOffset now) => new() { UserId = userId, UpdatedAt = now };

    public void SetSystemPrompt(string? systemPrompt, DateTimeOffset now)
    {
        SystemPrompt = DomainGuard.OptionalText(systemPrompt, Project.SystemPromptMaxLength, "systemPrompt");
        UpdatedAt = now;
    }
}
