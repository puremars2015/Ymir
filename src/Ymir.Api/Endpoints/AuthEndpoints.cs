using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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
    /// <summary>開發用登入的 issuer；正式 OIDC（Sprint 2）使用 IdP 的 issuer。</summary>
    public const string DevIssuer = "urn:ymir:dev";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints, IHostEnvironment environment)
    {
        endpoints.MapGet("/api/me", GetMeAsync).WithName("GetMe").WithTags("Auth").Produces<MeResponse>();

        endpoints.MapPost("/api/auth/logout", LogoutAsync).WithName("Logout").WithTags("Auth").RequireAntiforgeryHeader().Produces(StatusCodes.Status204NoContent);

        if (environment.IsDevelopment())
        {
            // 只在 Development 註冊：以帳號名稱直接登入，不需要企業 IdP（ADR-0002）。
            endpoints.MapPost("/api/dev/login", DevLoginAsync).WithName("DevLogin").WithTags("Auth").AllowAnonymous().Produces<MeResponse>();
        }

        return endpoints;
    }

    private static async Task<IResult> GetMeAsync(ICurrentUser currentUser, IUserDirectory users, CancellationToken cancellationToken)
    {
        var user = await users.FindAsync(currentUser.UserId, cancellationToken);
        return user is null
            ? ApiProblem.Create(StatusCodes.Status401Unauthorized, ExecutionErrorCodes.AuthRequired, "請先登入。")
            : TypedResults.Ok(MeResponse.From(user));
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

        var principal = YmirClaims.CreatePrincipal(user, CookieAuthenticationDefaults.AuthenticationScheme);
        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        httpContext.User = principal; // antiforgery token 綁定使用者，登入後立即換發
        AuthSetup.IssueXsrfToken(httpContext);

        await auditLog.WriteAsync(new AuditEntry($"user:{user.Id:D}", "auth.login", "user", user.Id.ToString("D"), AuditResult.Success, timeProvider.GetUtcNow(), null), cancellationToken);
        return TypedResults.Ok(MeResponse.From(user));
    }

    private static async Task<IResult> LogoutAsync(HttpContext httpContext, ICurrentUser currentUser, IAuditLog auditLog, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var actor = currentUser.ActorName;
        var userId = currentUser.UserId.ToString("D");
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        httpContext.Response.Cookies.Delete(AuthSetup.XsrfCookieName);
        await auditLog.WriteAsync(new AuditEntry(actor, "auth.logout", "user", userId, AuditResult.Success, timeProvider.GetUtcNow(), null), cancellationToken);
        return TypedResults.NoContent();
    }
}

public sealed record DevLoginRequest(string? Account, string? DisplayName, UserRole? Role);

public sealed record MeResponse(Guid Id, string DisplayName, string? AccountName, string? Email, string? Department, UserRole Role, UserStatus Status)
{
    public static MeResponse From(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new(user.Id, user.DisplayName, user.AccountName, user.Email, user.Department, user.Role, user.Status);
    }
}
