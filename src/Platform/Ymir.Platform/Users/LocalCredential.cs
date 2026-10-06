namespace Ymir.Platform.Users;

/// <summary>
/// 本機帳號的密碼（ADR-0009）：只存 PBKDF2 雜湊，不存明文。使用者本身仍是 <see cref="User"/>，
/// issuer 為 <see cref="LocalAccounts.Issuer"/>、subject 為正規化後的帳號名稱。
/// </summary>
public sealed class LocalCredential
{
    private LocalCredential()
    {
    }

    public Guid UserId { get; private set; }

    public string PasswordHash { get; private set; } = string.Empty;

    public int FailedAttempts { get; private set; }

    public DateTimeOffset? LockoutUntil { get; private set; }

    /// <summary>Admin 建立或重設密碼後為 true：使用者登入後必須先改密碼。</summary>
    public bool MustChangePassword { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static LocalCredential Create(Guid userId, string passwordHash, bool mustChangePassword, DateTimeOffset now) =>
        new() { UserId = userId, PasswordHash = passwordHash, MustChangePassword = mustChangePassword, UpdatedAt = now };

    public bool IsLockedOut(DateTimeOffset now) => LockoutUntil is { } until && until > now;

    public void SetPassword(string passwordHash, bool mustChangePassword, DateTimeOffset now)
    {
        PasswordHash = passwordHash;
        MustChangePassword = mustChangePassword;
        FailedAttempts = 0;
        LockoutUntil = null;
        UpdatedAt = now;
    }

    /// <summary>連續失敗達上限就鎖定一段時間，減緩線上猜密碼（SA §12）。</summary>
    public void RecordFailure(int maxAttempts, TimeSpan lockout, DateTimeOffset now)
    {
        FailedAttempts++;
        if (FailedAttempts >= maxAttempts)
        {
            LockoutUntil = now + lockout;
            FailedAttempts = 0;
        }

        UpdatedAt = now;
    }

    public void RecordSuccess(DateTimeOffset now)
    {
        if (FailedAttempts != 0 || LockoutUntil is not null)
        {
            FailedAttempts = 0;
            LockoutUntil = null;
            UpdatedAt = now;
        }
    }
}
