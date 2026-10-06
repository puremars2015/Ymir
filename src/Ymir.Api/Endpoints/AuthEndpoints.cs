using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.Platform.Auditing;
using Ymir.Platform.Identity;
using Ymir.Platform.Infrastructure.Identity;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Contracts.Executions;

namespace Ymir.Api.Endpoints;

internal static class AuthEndpoints
{
    /// <summary>開發用登入的 issuer。</summary>
    public const string DevIssuer = "urn:ymir:dev";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints, IHostEnvironment environment)
    {
        endpoints.MapGet("/api/me", GetMeAsync).WithName("GetMe").WithTags("Auth").Produces<MeResponse>();

        endpoints.MapPost("/api/auth/logout", LogoutAsync).WithName("Logout").WithTags("Auth").RequireAntiforgeryHeader().Produces(StatusCodes.Status204NoContent);

        // 登入頁依此顯示可用的登入方式（ADR-0009）。
        endpoints.MapGet("/api/auth/providers", GetProviders).WithName("GetLoginProviders").WithTags("Auth").AllowAnonymous().Produces<LoginProvidersResponse>();

        // 企業帳號：整頁導向（不是 XHR），由後端完成 OIDC（ADR-0002、ADR-0009）。
        endpoints.MapGet("/api/auth/login", ChallengeOidc).WithName("LoginWithOidc").WithTags("Auth").AllowAnonymous()
            .Produces(StatusCodes.Status302Found).Produces(StatusCodes.Status404NotFound).ExcludeFromDescription();

        endpoints.MapPost("/api/auth/password-login", PasswordLoginAsync).WithName("PasswordLogin").WithTags("Auth").AllowAnonymous()
            .RequireAntiforgeryHeader().RequireRateLimiting(AuthSetup.PasswordLoginRateLimit).Produces<MeResponse>();

        endpoints.MapPost("/api/me/password", ChangePasswordAsync).WithName("ChangePassword").WithTags("Auth").RequireAntiforgeryHeader()
            .Produces(StatusCodes.Status204NoContent);

        if (environment.IsDevelopment())
        {
            // 只在 Development 註冊：以帳號名稱直接登入，不需要企業 IdP（ADR-0002）。
            endpoints.MapPost("/api/dev/login", DevLoginAsync).WithName("DevLogin").WithTags("Auth").AllowAnonymous().Produces<MeResponse>();
        }

        return endpoints;
    }

    private static async Task<IResult> GetMeAsync(ICurrentUser currentUser, IUserDirectory users, ILocalAccountService localAccounts, CancellationToken cancellationToken)
    {
        var user = await users.FindAsync(currentUser.UserId, cancellationToken);
        if (user is null)
        {
            return ApiProblem.Create(StatusCodes.Status401Unauthorized, ExecutionErrorCodes.AuthRequired, "請先登入。");
        }

        var local = user.Issuer == LocalAccounts.Issuer ? await localAccounts.GetStatusAsync(user.Id, cancellationToken) : null;
        return TypedResults.Ok(MeResponse.From(user, local?.MustChangePassword ?? false));
    }

    private static LoginProvidersResponse GetProviders(IOptions<YmirAuthOptions> options, IHostEnvironment environment)
    {
        var auth = options.Value;
        return new LoginProvidersResponse(
            auth.Oidc.IsConfigured,
            auth.Oidc.IsConfigured ? auth.Oidc.DisplayName : null,
            auth.LocalAccounts.Enabled,
            environment.IsDevelopment());
    }

    private static IResult ChallengeOidc(string? returnUrl, IOptions<YmirAuthOptions> options)
    {
        if (!options.Value.Oidc.IsConfigured)
        {
            return ApiProblem.Create(StatusCodes.Status404NotFound, "OIDC_NOT_CONFIGURED", "未設定企業帳號登入。");
        }

        return Results.Challenge(new AuthenticationProperties { RedirectUri = SafeRedirect.LocalPathOrRoot(returnUrl) }, [AuthSetup.OidcScheme]);
    }

    private static async Task<IResult> PasswordLoginAsync(
        PasswordLoginRequest request,
        HttpContext httpContext,
        IOptions<YmirAuthOptions> options,
        ILocalAccountService localAccounts,
        IAuditLog auditLog,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!options.Value.LocalAccounts.Enabled)
        {
            return ApiProblem.Create(StatusCodes.Status404NotFound, "PASSWORD_LOGIN_DISABLED", "未開放帳號密碼登入。");
        }

        var result = await localAccounts.VerifyAsync(request.Account ?? string.Empty, request.Password ?? string.Empty, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var target = LocalAccounts.NormalizeAccount(request.Account) ?? "invalid";
        switch (result.Outcome)
        {
            case LocalSignInOutcome.Success:
                break;
            case LocalSignInOutcome.LockedOut:
                await auditLog.WriteAsync(new AuditEntry("anonymous", "auth.login.password", "local-account", target, AuditResult.Denied, now, null), cancellationToken);
                return ApiProblem.Create(StatusCodes.Status429TooManyRequests, "ACCOUNT_LOCKED", "嘗試次數過多，請 15 分鐘後再試。");
            case LocalSignInOutcome.Disabled:
                await auditLog.WriteAsync(new AuditEntry($"user:{result.User!.Id:D}", "auth.login.password", "user", result.User.Id.ToString("D"), AuditResult.Denied, now, null), cancellationToken);
                return ApiProblem.Create(StatusCodes.Status403Forbidden, ExecutionErrorCodes.Forbidden, "帳號已停用。");
            default:
                // 帳號不存在與密碼錯誤回應相同，避免列舉帳號。
                await auditLog.WriteAsync(new AuditEntry("anonymous", "auth.login.password", "local-account", target, AuditResult.Failure, now, null), cancellationToken);
                return ApiProblem.Create(StatusCodes.Status401Unauthorized, "INVALID_CREDENTIALS", "帳號或密碼錯誤。");
        }

        var user = result.User!;
        await SignInAsync(httpContext, user, result.MustChangePassword);
        await auditLog.WriteAsync(new AuditEntry($"user:{user.Id:D}", "auth.login.password", "user", user.Id.ToString("D"), AuditResult.Success, now, null), cancellationToken);
        return TypedResults.Ok(MeResponse.From(user, result.MustChangePassword));
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        HttpContext httpContext,
        ICurrentUser currentUser,
        IUserDirectory users,
        ILocalAccountService localAccounts,
        IAuditLog auditLog,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var result = await localAccounts.ChangePasswordAsync(userId, request.CurrentPassword ?? string.Empty, request.NewPassword ?? string.Empty, cancellationToken);
        var auditResult = result.Succeeded ? AuditResult.Success : AuditResult.Failure;
        await auditLog.WriteAsync(new AuditEntry(currentUser.ActorName, "auth.password.change", "user", userId.ToString("D"), auditResult, timeProvider.GetUtcNow(), null), cancellationToken);
        switch (result.Outcome)
        {
            case LocalAccountOutcome.Success:
                // 換發不含「必須改密碼」的 cookie。
                var user = await users.FindAsync(userId, cancellationToken);
                await SignInAsync(httpContext, user!, mustChangePassword: false);
                return TypedResults.NoContent();
            case LocalAccountOutcome.NotLocalAccount:
                return ApiProblem.Create(StatusCodes.Status400BadRequest, "NOT_LOCAL_ACCOUNT", "企業帳號請到公司的帳號系統變更密碼。");
            case LocalAccountOutcome.WeakPassword:
                return ApiProblem.Create(StatusCodes.Status400BadRequest, "WEAK_PASSWORD", $"新密碼至少 {LocalAccounts.MinimumPasswordLength} 個字元，且不能與目前密碼相同。");
            default:
                return ApiProblem.Create(StatusCodes.Status400BadRequest, "INVALID_PASSWORD", "目前密碼不正確。");
        }
    }

    private static async Task<IResult> DevLoginAsync(
        DevLoginRequest request,
        HttpContext httpContext,
        IUserDirectory users,
        IAuditLog auditLog,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var account = request.Account?.Trim();
        if (string.IsNullOrEmpty(account) || account.Length > 100)
        {
            return ApiProblem.Create(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", "account 必須是 1～100 個字元。");
        }

        var identity = new ExternalIdentity(DevIssuer, account, request.DisplayName?.Trim() is { Length: > 0 } name ? name : account, account, null, "Development");
        var user = await users.UpsertOnLoginAsync(identity, request.Role ?? UserRole.User, cancellationToken);
        if (user.Status == UserStatus.Disabled)
        {
            await auditLog.WriteAsync(new AuditEntry($"user:{user.Id:D}", "auth.login", "user", user.Id.ToString("D"), AuditResult.Denied, timeProvider.GetUtcNow(), null), cancellationToken);
            return ApiProblem.Create(StatusCodes.Status403Forbidden, ExecutionErrorCodes.Forbidden, "帳號已停用。");
        }

        await SignInAsync(httpContext, user, mustChangePassword: false);
        await auditLog.WriteAsync(new AuditEntry($"user:{user.Id:D}", "auth.login", "user", user.Id.ToString("D"), AuditResult.Success, timeProvider.GetUtcNow(), null), cancellationToken);
        return TypedResults.Ok(MeResponse.From(user, mustChangePassword: false));
    }

    private static async Task<IResult> LogoutAsync(HttpContext httpContext, ICurrentUser currentUser, IAuditLog auditLog, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var actor = currentUser.ActorName;
        var userId = currentUser.UserId.ToString("D");
        // 只登出 Ymir；企業帳號的 SSO session 保留（ADR-0009）。
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        httpContext.Response.Cookies.Delete(AuthSetup.XsrfCookieName);
        await auditLog.WriteAsync(new AuditEntry(actor, "auth.logout", "user", userId, AuditResult.Success, timeProvider.GetUtcNow(), null), cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task SignInAsync(HttpContext httpContext, User user, bool mustChangePassword)
    {
        var principal = YmirClaims.CreatePrincipal(user, CookieAuthenticationDefaults.AuthenticationScheme, mustChangePassword);
        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        httpContext.User = principal; // antiforgery token 綁定使用者，登入後立即換發
        AuthSetup.IssueXsrfToken(httpContext);
    }
}

public sealed record DevLoginRequest(string? Account, string? DisplayName, UserRole? Role);

public sealed record PasswordLoginRequest(string? Account, string? Password);

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

/// <param name="Oidc">是否可以用企業帳號（Entra ID）登入。</param>
/// <param name="Password">是否可以用本機帳號密碼登入。</param>
/// <param name="DevLogin">Development 的免密碼登入。</param>
public sealed record LoginProvidersResponse(bool Oidc, string? OidcDisplayName, bool Password, bool DevLogin);

/// <summary>登入方式：企業帳號、本機帳號、開發登入。</summary>
public enum AuthMethod
{
    Oidc = 0,
    Local = 1,
    Dev = 2,
}

public sealed record MeResponse(
    Guid Id,
    string DisplayName,
    string? AccountName,
    string? Email,
    string? Department,
    UserRole Role,
    UserStatus Status,
    AuthMethod AuthMethod,
    bool MustChangePassword)
{
    public static MeResponse From(User user, bool mustChangePassword)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new(user.Id, user.DisplayName, user.AccountName, user.Email, user.Department, user.Role, user.Status, AuthMethodOf(user), mustChangePassword);
    }

    public static AuthMethod AuthMethodOf(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.Issuer switch
        {
            LocalAccounts.Issuer => AuthMethod.Local,
            AuthEndpoints.DevIssuer => AuthMethod.Dev,
            _ => AuthMethod.Oidc,
        };
    }
}
