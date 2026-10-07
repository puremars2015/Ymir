namespace Ymir.VibeMaker.Domain;

public enum McpAccessMode
{
    /// <summary>所有使用者。</summary>
    Everyone,

    /// <summary>只有 Admin。</summary>
    AdminsOnly,

    /// <summary>只有 <see cref="McpServerAccess.UserIds"/> 中的使用者。</summary>
    SelectedUsers,
}

/// <summary>
/// 平台 MCP 服務的存取清單（ADR-0012 B.4）：管理員只能啟用 / 停用與設定對象，不能新增服務或位址。
/// 目錄中沒有對應紀錄的服務視為停用。跨模組只存 user id（ADR-0001）。
/// </summary>
public sealed class McpServerAccess
{
    public const int MaxUsers = 500;

    private McpServerAccess()
    {
    }

    public string ServerName { get; private set; } = string.Empty;

    public bool Enabled { get; private set; }

    public McpAccessMode Mode { get; private set; }

    /// <summary>以逗號分隔的 user id（<see cref="McpAccessMode.SelectedUsers"/> 使用）。</summary>
    public string UserIdList { get; private set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<Guid> UserIds =>
        [.. UserIdList.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse)];

    public static McpServerAccess Create(string serverName, DateTimeOffset now) =>
        new() { ServerName = DomainGuard.RequiredText(serverName, 40, nameof(serverName)), Mode = McpAccessMode.Everyone, UpdatedAt = now };

    public void Update(bool enabled, McpAccessMode mode, IReadOnlyCollection<Guid> userIds, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (userIds.Count > MaxUsers)
        {
            throw new DomainValidationException($"At most {MaxUsers} users.");
        }

        Enabled = enabled;
        Mode = mode;
        UserIdList = string.Join(',', userIds.Distinct().Select(id => id.ToString("D")));
        UpdatedAt = now;
    }

    public bool Allows(Guid userId, bool isAdmin) => Enabled && Mode switch
    {
        McpAccessMode.Everyone => true,
        McpAccessMode.AdminsOnly => isAdmin,
        _ => UserIds.Contains(userId),
    };
}
