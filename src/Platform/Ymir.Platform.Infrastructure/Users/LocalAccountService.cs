using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Infrastructure.Persistence;
using Ymir.Platform.Users;

namespace Ymir.Platform.Infrastructure.Users;

/// <summary>
/// 本機帳號（ADR-0009）。密碼以 ASP.NET Core Identity 的 <see cref="PasswordHasher{TUser}"/> 雜湊（PBKDF2-HMAC-SHA512，含 salt），
/// 演算法升級時登入成功會自動重新雜湊。帳號不存在時仍計算一次雜湊，回應時間不洩漏帳號是否存在。
/// </summary>
internal sealed class LocalAccountService(PlatformDbContext db, IUserDirectory users, TimeProvider timeProvider) : ILocalAccountService
{
    private static readonly PasswordHasher<LocalCredential> s_hasher = new();

    // 帳號不存在時用來比對的假雜湊（與真實雜湊相同成本）。
    private static readonly string s_dummyHash = s_hasher.HashPassword(null!, Guid.NewGuid().ToString("N"));

    public async Task<LocalAccountResult> CreateAsync(CreateLocalAccount request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var account = LocalAccounts.NormalizeAccount(request.Account);
        var displayName = request.DisplayName?.Trim();
        if (account is null || string.IsNullOrEmpty(displayName) || displayName.Length > 200 || request.Email?.Length > 320)
        {
            return new LocalAccountResult(LocalAccountOutcome.InvalidAccount);
        }

        if (!LocalAccounts.IsStrongEnough(request.Password))
        {
            return new LocalAccountResult(LocalAccountOutcome.WeakPassword);
        }

        var exists = await db.Users.AnyAsync(u => u.Issuer == LocalAccounts.Issuer && u.Subject == account, cancellationToken).ConfigureAwait(false);
        if (exists)
        {
            return new LocalAccountResult(LocalAccountOutcome.AccountExists);
        }

        var now = timeProvider.GetUtcNow();
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        var user = User.Create(LocalAccounts.IdentityFor(account, displayName, email), request.Role, now);
        db.Users.Add(user);
        // Admin 設定的初始密碼：使用者第一次登入必須改掉。
        db.LocalCredentials.Add(LocalCredential.Create(user.Id, s_hasher.HashPassword(null!, request.Password), mustChangePassword: true, now));
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // 同時建立同名帳號：唯一索引擋下。
            return new LocalAccountResult(LocalAccountOutcome.AccountExists);
        }

        return new LocalAccountResult(LocalAccountOutcome.Success, user);
    }

    public async Task<LocalSignInResult> VerifyAsync(string account, string password, CancellationToken cancellationToken)
    {
        var normalized = LocalAccounts.NormalizeAccount(account);
        var now = timeProvider.GetUtcNow();
        var user = normalized is null
            ? null
            : await db.Users.SingleOrDefaultAsync(u => u.Issuer == LocalAccounts.Issuer && u.Subject == normalized, cancellationToken).ConfigureAwait(false);
        var credential = user is null
            ? null
            : await db.LocalCredentials.SingleOrDefaultAsync(c => c.UserId == user.Id, cancellationToken).ConfigureAwait(false);

        if (user is null || credential is null || string.IsNullOrEmpty(password) || password.Length > LocalAccounts.MaximumPasswordLength)
        {
            s_hasher.VerifyHashedPassword(null!, s_dummyHash, password ?? string.Empty);
            return new LocalSignInResult(LocalSignInOutcome.InvalidCredentials);
        }

        if (credential.IsLockedOut(now))
        {
            return new LocalSignInResult(LocalSignInOutcome.LockedOut);
        }

        var verification = s_hasher.VerifyHashedPassword(null!, credential.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            credential.RecordFailure(LocalAccounts.MaxFailedAttempts, LocalAccounts.LockoutDuration, now);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new LocalSignInResult(credential.IsLockedOut(now) ? LocalSignInOutcome.LockedOut : LocalSignInOutcome.InvalidCredentials);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            credential.SetPassword(s_hasher.HashPassword(null!, password), credential.MustChangePassword, now);
        }

        credential.RecordSuccess(now);
        if (user.Status == UserStatus.Disabled)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new LocalSignInResult(LocalSignInOutcome.Disabled, user);
        }

        user.RecordLogin(LocalAccounts.IdentityFor(normalized!, user.DisplayName, user.Email), now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new LocalSignInResult(LocalSignInOutcome.Success, user, credential.MustChangePassword);
    }

    public async Task<LocalAccountResult> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        var credential = await db.LocalCredentials.SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (credential is null)
        {
            return new LocalAccountResult(LocalAccountOutcome.NotLocalAccount);
        }

        var now = timeProvider.GetUtcNow();
        if (credential.IsLockedOut(now)
            || string.IsNullOrEmpty(currentPassword)
            || currentPassword.Length > LocalAccounts.MaximumPasswordLength
            || s_hasher.VerifyHashedPassword(null!, credential.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
        {
            if (!credential.IsLockedOut(now))
            {
                credential.RecordFailure(LocalAccounts.MaxFailedAttempts, LocalAccounts.LockoutDuration, now);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            return new LocalAccountResult(LocalAccountOutcome.InvalidPassword);
        }

        if (!LocalAccounts.IsStrongEnough(newPassword) || newPassword == currentPassword)
        {
            return new LocalAccountResult(LocalAccountOutcome.WeakPassword);
        }

        credential.SetPassword(s_hasher.HashPassword(null!, newPassword), mustChangePassword: false, now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new LocalAccountResult(LocalAccountOutcome.Success);
    }

    public async Task<LocalAccountResult> ResetPasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken)
    {
        var user = await users.FindAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return new LocalAccountResult(LocalAccountOutcome.NotFound);
        }

        var credential = await db.LocalCredentials.SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (credential is null)
        {
            return new LocalAccountResult(LocalAccountOutcome.NotLocalAccount);
        }

        if (!LocalAccounts.IsStrongEnough(newPassword))
        {
            return new LocalAccountResult(LocalAccountOutcome.WeakPassword);
        }

        credential.SetPassword(s_hasher.HashPassword(null!, newPassword), mustChangePassword: true, timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new LocalAccountResult(LocalAccountOutcome.Success, user);
    }

    public async Task<LocalCredentialStatus?> GetStatusAsync(Guid userId, CancellationToken cancellationToken)
    {
        var mustChange = await db.LocalCredentials.AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => (bool?)c.MustChangePassword)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return mustChange is { } value ? new LocalCredentialStatus(value) : null;
    }
}
