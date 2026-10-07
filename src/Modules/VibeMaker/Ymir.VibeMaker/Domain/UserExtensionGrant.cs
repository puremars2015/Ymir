namespace Ymir.VibeMaker.Domain;

/// <summary>管理員可控制的 Agent 擴充能力（ADR-0012 A.1）。</summary>
public enum ExtensionCapability
{
    /// <summary>使用者自建的 skill（<c>SKILL.md</c>）。</summary>
    Skills,

    /// <summary>使用者自建的 MCP server。</summary>
    Mcp,
}

/// <summary>每人覆寫的效果；沒有資料列表示繼承全域預設。</summary>
public enum ExtensionGrantEffect
{
    Allow,
    Deny,
}

/// <summary>
/// 管理員對某位成員的擴充能力覆寫（ADR-0012 A.2）。一位成員每種能力最多一筆；
/// 跨模組只存 user id、不建 FK（ADR-0001）。
/// </summary>
public sealed class UserExtensionGrant
{
    public const int UpdatedByMaxLength = 200;

    private UserExtensionGrant()
    {
    }

    public Guid UserId { get; private set; }

    public ExtensionCapability Capability { get; private set; }

    public ExtensionGrantEffect Effect { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>稽核用的 actor（管理員）。</summary>
    public string UpdatedBy { get; private set; } = string.Empty;

    public static UserExtensionGrant Create(Guid userId, ExtensionCapability capability, ExtensionGrantEffect effect, string updatedBy, DateTimeOffset now)
    {
        var grant = new UserExtensionGrant { UserId = userId, Capability = capability };
        grant.Set(effect, updatedBy, now);
        return grant;
    }

    public void Set(ExtensionGrantEffect effect, string updatedBy, DateTimeOffset now)
    {
        Effect = effect;
        UpdatedBy = DomainGuard.RequiredText(updatedBy, UpdatedByMaxLength, nameof(updatedBy));
        UpdatedAt = now;
    }
}
