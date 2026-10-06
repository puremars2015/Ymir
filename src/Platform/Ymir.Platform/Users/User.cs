using Ymir.Platform.Identity;

namespace Ymir.Platform.Users;

/// <summary>
/// Ymir 使用者（SA §8 USER）。外部身分以 (<see cref="Issuer"/>, <see cref="Subject"/>) 唯一識別（ADR-0002），
/// <see cref="AccountName"/> 只用於顯示，可能更名。
/// </summary>
public sealed class User
{
    private User()
    {
    }

    public Guid Id { get; private set; }

    public string Issuer { get; private set; } = string.Empty;

    public string Subject { get; private set; } = string.Empty;

    public string? AccountName { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public string? Email { get; private set; }

    public string? Department { get; private set; }

    public UserRole Role { get; private set; }

    public UserStatus Status { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static User Create(ExternalIdentity identity, UserRole role, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var user = new User
        {
            Id = Guid.CreateVersion7(now),
            Issuer = identity.Issuer,
            Subject = identity.Subject,
            Role = role,
            Status = UserStatus.Active,
            CreatedAt = now,
        };
        user.ApplyProfile(identity, now);
        return user;
    }

    /// <summary>每次登入同步 IdP 上的顯示資料（SA §4.1 Upsert USER）。角色與狀態由 Ymir 管理，不被 IdP 覆寫。</summary>
    public void RecordLogin(ExternalIdentity identity, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ApplyProfile(identity, now);
        LastLoginAt = now;
    }

    public void Disable(DateTimeOffset now)
    {
        Status = UserStatus.Disabled;
        UpdatedAt = now;
    }

    public void Enable(DateTimeOffset now)
    {
        Status = UserStatus.Active;
        UpdatedAt = now;
    }

    /// <summary>
    /// 角色以企業 IdP 為準時（Entra ID app role，ADR-0009），每次登入同步；IdP 拿掉角色後下次登入就降級。
    /// 狀態（停用）仍由 Ymir 管理，不受 IdP 影響。
    /// </summary>
    public void SyncRole(UserRole role, DateTimeOffset now)
    {
        if (Role != role)
        {
            Role = role;
            UpdatedAt = now;
        }
    }

    private void ApplyProfile(ExternalIdentity identity, DateTimeOffset now)
    {
        AccountName = identity.AccountName;
        DisplayName = identity.DisplayName;
        Email = identity.Email;
        Department = identity.Department;
        UpdatedAt = now;
    }
}
