namespace Ymir.VibeMaker.Domain;

/// <summary>
/// 使用者自己的外掛開關（ADR-0030 §3）：管理員決定「誰能用」，這裡記錄使用者「要不要用」。
/// 沒有紀錄時使用外掛的預設值（OneDrive 關閉、平台 MCP 開啟）。跨模組只存 user id（ADR-0001）。
/// </summary>
public sealed class UserPluginSetting
{
    public const int KeyMaxLength = 60;

    private UserPluginSetting()
    {
    }

    public Guid UserId { get; private set; }

    /// <summary><c>onedrive</c> 或 <c>mcp:&lt;服務名稱&gt;</c>；由 <c>PluginService</c> 驗證是清單內的 key。</summary>
    public string PluginKey { get; private set; } = string.Empty;

    public bool Enabled { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static UserPluginSetting Create(Guid userId, string pluginKey, bool enabled, DateTimeOffset now) => new()
    {
        UserId = userId,
        PluginKey = DomainGuard.RequiredText(pluginKey, KeyMaxLength, nameof(pluginKey)),
        Enabled = enabled,
        UpdatedAt = now,
    };

    public void Set(bool enabled, DateTimeOffset now)
    {
        Enabled = enabled;
        UpdatedAt = now;
    }
}
