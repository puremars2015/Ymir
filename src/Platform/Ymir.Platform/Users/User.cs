using Ymir.Platform.Identity;

namespace Ymir.Platform.Users;

/// <summary>
/// Ymir 使用者（SA §8 USER）。外部身分以 (<see cref="Issuer"/>, <see cref="Subject"/>) 唯一識別（ADR-0002），
/// <see cref="AccountName"/> 只用於顯示，可能更名。
/// </summary>
public sealed class User
{
    /// <summary>刪除後外部身分已釋放的 issuer（ADR-0027）。</summary>
    public const string DeletedIssuer = "urn:ymir:deleted";

    private User()
    {
    }

    public Guid Id { get; private set; }

    public string Issuer { get; private set; } = string.Empty;

    public string Subject { get; private set; } = string.Empty;

    public string? AccountName { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Admin 設定過顯示名稱（ADR-0027）：之後登入不再以 IdP 的名稱覆寫。</summary>
    public bool DisplayNameOverridden { get; private set; }

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
    /// 角色以企業 IdP 為準時（Entra ID app role、Google AdminEmails，ADR-0009 / ADR-0024），每次登入同步「是否為 Admin」：
    /// IdP 給 Admin 就升級，IdP 拿掉 Admin 後下次登入降為 <paramref name="role"/>。
    /// 非 Admin 之間（User / Guest）由 Ymir 管理（ADR-0027），IdP 不會把 Admin 設定的訪客改回使用者。
    /// 狀態（停用）仍由 Ymir 管理，不受 IdP 影響。
    /// </summary>
    public void SyncRole(UserRole role, DateTimeOffset now)
    {
        var synced = role == UserRole.Admin || Role == UserRole.Admin ? role : Role;
        if (Role != synced)
        {
            Role = synced;
            UpdatedAt = now;
        }
    }

    /// <summary>Admin 變更角色（ADR-0027）；IdP 管理的帳號能否變更由呼叫端檢查。</summary>
    public void ChangeRole(UserRole role, DateTimeOffset now)
    {
        Role = role;
        UpdatedAt = now;
    }

    /// <summary>Admin 設定顯示名稱（ADR-0027）；之後登入不再被 IdP 覆寫。</summary>
    public void Rename(string displayName, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName;
        DisplayNameOverridden = true;
        UpdatedAt = now;
    }

    /// <summary>
    /// 刪除帳號（ADR-0027）：其他資料表以 user id 參照，所以只標示刪除、保留顯示名稱；
    /// (issuer, subject) 換成不會被登入用到的值，同一外部身分或本機帳號名稱之後可以重新建立成新的使用者。
    /// </summary>
    public void Delete(DateTimeOffset now)
    {
        Status = UserStatus.Deleted;
        Issuer = DeletedIssuer;
        Subject = Id.ToString("N");
        Email = null;
        UpdatedAt = now;
    }

    private void ApplyProfile(ExternalIdentity identity, DateTimeOffset now)
    {
        AccountName = identity.AccountName;
        if (!DisplayNameOverridden)
        {
            DisplayName = identity.DisplayName;
        }

        Email = identity.Email;
        Department = identity.Department;
        UpdatedAt = now;
    }
}
