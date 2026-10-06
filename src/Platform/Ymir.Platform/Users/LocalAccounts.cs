using Ymir.Platform.Identity;

namespace Ymir.Platform.Users;

/// <summary>本機帳號（帳號密碼登入，ADR-0009）：給沒有企業帳號的人使用，由 Admin 建立。</summary>
public interface ILocalAccountService
{
    Task<LocalAccountResult> CreateAsync(CreateLocalAccount request, CancellationToken cancellationToken);

    /// <summary>驗證帳號密碼。帳號不存在與密碼錯誤回傳相同結果，避免列舉帳號。</summary>
    Task<LocalSignInResult> VerifyAsync(string account, string password, CancellationToken cancellationToken);

    Task<LocalAccountResult> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken);

    /// <summary>Admin 重設密碼；使用者下次登入必須先改密碼。</summary>
    Task<LocalAccountResult> ResetPasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken);

    /// <summary>不是本機帳號時回傳 null。</summary>
    Task<LocalCredentialStatus?> GetStatusAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed record CreateLocalAccount(string Account, string DisplayName, string? Email, UserRole Role, string Password);

public sealed record LocalCredentialStatus(bool MustChangePassword);

public enum LocalAccountOutcome
{
    Success = 0,
    AccountExists = 1,
    InvalidAccount = 2,
    WeakPassword = 3,
    InvalidPassword = 4,
    NotLocalAccount = 5,
    NotFound = 6,
}

public sealed record LocalAccountResult(LocalAccountOutcome Outcome, User? User = null)
{
    public bool Succeeded => Outcome == LocalAccountOutcome.Success;
}

public enum LocalSignInOutcome
{
    Success = 0,
    InvalidCredentials = 1,
    LockedOut = 2,
    Disabled = 3,
}

public sealed record LocalSignInResult(LocalSignInOutcome Outcome, User? User = null, bool MustChangePassword = false);

public static class LocalAccounts
{
    /// <summary>本機帳號的 issuer；與企業 IdP 的 issuer 不會衝突。</summary>
    public const string Issuer = "urn:ymir:local";

    public const int MinimumPasswordLength = 12;

    public const int MaximumPasswordLength = 256;

    public const int MaxFailedAttempts = 5;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>帳號：3～64 個英數字或 <c>. _ -</c>，不分大小寫（以小寫儲存）。</summary>
    public static string? NormalizeAccount(string? account)
    {
        var value = account?.Trim().ToLowerInvariant();
        return value is { Length: >= 3 and <= 64 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
            ? value
            : null;
    }

    public static bool IsStrongEnough(string? password) =>
        password is { Length: >= MinimumPasswordLength and <= MaximumPasswordLength } && !string.IsNullOrWhiteSpace(password);

    public static ExternalIdentity IdentityFor(string normalizedAccount, string displayName, string? email) =>
        new(Issuer, normalizedAccount, displayName, normalizedAccount, email, null);
}
