namespace Ymir.VibeMaker.Contracts.Settings;

/// <summary><c>/api/me/settings</c>：目前使用者的個人設定。</summary>
/// <param name="SystemPrompt">個人 global system prompt；null 表示未設定。</param>
public sealed record UserSettingsResponse(string? SystemPrompt);

/// <param name="SystemPrompt">空白或 null 表示清除。上限 10,000 字。</param>
public sealed record UpdateUserSettingsRequest(string? SystemPrompt);
