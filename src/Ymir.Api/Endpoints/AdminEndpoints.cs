using Microsoft.Extensions.Options;
using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.Platform.Auditing;
using Ymir.Platform.Identity;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Application.Users;

namespace Ymir.Api.Endpoints;

/// <summary>
/// Admin：使用者管理（SA §4、ADR-0009）。停用 / 啟用、建立本機帳號、重設本機帳號密碼。
/// 企業帳號的角色以 Entra app role 為準，不在這裡修改。
/// </summary>
internal static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/users").WithTags("Admin").RequireAuthorization(AuthSetup.AdminPolicy).RequireAntiforgeryHeader();

        group.MapGet("/", ListAsync).WithName("AdminListUsers").Produces<List<AdminUserResponse>>();
        group.MapPost("/", CreateLocalAsync).WithName("AdminCreateLocalUser").Produces<AdminUserResponse>(StatusCodes.Status201Created);
        group.MapPost("/{userId:guid}/disable", DisableAsync).WithName("AdminDisableUser").Produces<AdminUserResponse>();
        group.MapPost("/{userId:guid}/enable", EnableAsync).WithName("AdminEnableUser").Produces<AdminUserResponse>();
        group.MapPost("/{userId:guid}/reset-password", ResetPasswordAsync).WithName("AdminResetPassword").Produces(StatusCodes.Status204NoContent);
        return endpoints;
    }

    private static async Task<List<AdminUserResponse>> ListAsync(string? search, IUserDirectory users, CancellationToken cancellationToken) =>
        [.. (await users.ListAsync(search, 200, cancellationToken)).Select(AdminUserResponse.From)];

    private static async Task<IResult> CreateLocalAsync(
        CreateLocalUserRequest request,
        IOptions<YmirAuthOptions> options,
        ILocalAccountService localAccounts,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!options.Value.LocalAccounts.Enabled)
        {
            return ApiProblem.Create(StatusCodes.Status400BadRequest, "PASSWORD_LOGIN_DISABLED", "未開放帳號密碼登入。");
        }

        var result = await localAccounts.CreateAsync(
            new CreateLocalAccount(request.Account ?? string.Empty, request.DisplayName ?? string.Empty, request.Email, request.Role ?? UserRole.User, request.InitialPassword ?? string.Empty),
            cancellationToken);
        switch (result.Outcome)
        {
            case LocalAccountOutcome.Success:
                var user = result.User!;
                await auditLog.WriteAsync(new AuditEntry(currentUser.ActorName, "admin.user.create", "user", user.Id.ToString("D"), AuditResult.Success, timeProvider.GetUtcNow(), null), cancellationToken);
                return TypedResults.Created($"/api/admin/users/{user.Id}", AdminUserResponse.From(user));
            case LocalAccountOutcome.AccountExists:
                return ApiProblem.Create(StatusCodes.Status409Conflict, "ACCOUNT_EXISTS", "帳號已存在。");
            case LocalAccountOutcome.WeakPassword:
                return ApiProblem.Create(StatusCodes.Status400BadRequest, "WEAK_PASSWORD", $"密碼至少 {LocalAccounts.MinimumPasswordLength} 個字元。");
            default:
                return ApiProblem.Create(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", "帳號必須是 3～64 個英數字或 . _ -，且必須填寫顯示名稱。");
        }
    }

    private static async Task<IResult> DisableAsync(
        Guid userId,
        ICurrentUser currentUser,
        IUserDirectory users,
        UserDeactivationService deactivation,
        IAuditLog auditLog,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (userId == currentUser.UserId)
        {
            return ApiProblem.Create(StatusCodes.Status400BadRequest, "CANNOT_DISABLE_SELF", "不能停用自己的帳號。");
        }

        var user = await users.SetStatusAsync(userId, UserStatus.Disabled, cancellationToken);
        if (user is null)
        {
            return UserNotFound();
        }

        // 停用後該使用者的下一個請求就會被擋下（CookiePrincipalValidator）；這裡再清理執行中的工作與金鑰。
        await deactivation.DeactivateAsync(userId, currentUser.ActorName, cancellationToken);
        await auditLog.WriteAsync(new AuditEntry(currentUser.ActorName, "admin.user.disable", "user", userId.ToString("D"), AuditResult.Success, timeProvider.GetUtcNow(), null), cancellationToken);
        return TypedResults.Ok(AdminUserResponse.From(user));
    }

    private static async Task<IResult> EnableAsync(
        Guid userId,
        ICurrentUser currentUser,
        IUserDirectory users,
        IAuditLog auditLog,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var user = await users.SetStatusAsync(userId, UserStatus.Active, cancellationToken);
        if (user is null)
        {
            return UserNotFound();
        }

        await auditLog.WriteAsync(new AuditEntry(currentUser.ActorName, "admin.user.enable", "user", userId.ToString("D"), AuditResult.Success, timeProvider.GetUtcNow(), null), cancellationToken);
        return TypedResults.Ok(AdminUserResponse.From(user));
    }

    private static async Task<IResult> ResetPasswordAsync(
        Guid userId,
        ResetPasswordRequest request,
        ICurrentUser currentUser,
        ILocalAccountService localAccounts,
        IAuditLog auditLog,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var result = await localAccounts.ResetPasswordAsync(userId, request.NewPassword ?? string.Empty, cancellationToken);
        switch (result.Outcome)
        {
            case LocalAccountOutcome.Success:
                await auditLog.WriteAsync(new AuditEntry(currentUser.ActorName, "admin.user.reset_password", "user", userId.ToString("D"), AuditResult.Success, timeProvider.GetUtcNow(), null), cancellationToken);
                return TypedResults.NoContent();
            case LocalAccountOutcome.NotFound:
                return UserNotFound();
            case LocalAccountOutcome.NotLocalAccount:
                return ApiProblem.Create(StatusCodes.Status400BadRequest, "NOT_LOCAL_ACCOUNT", "企業帳號的密碼由公司的帳號系統管理。");
            default:
                return ApiProblem.Create(StatusCodes.Status400BadRequest, "WEAK_PASSWORD", $"密碼至少 {LocalAccounts.MinimumPasswordLength} 個字元。");
        }
    }

    private static IResult UserNotFound() => ApiProblem.Create(StatusCodes.Status404NotFound, "USER_NOT_FOUND", "找不到使用者。");
}

public sealed record CreateLocalUserRequest(string? Account, string? DisplayName, string? Email, UserRole? Role, string? InitialPassword);

public sealed record ResetPasswordRequest(string? NewPassword);

public sealed record AdminUserResponse(
    Guid Id,
    string DisplayName,
    string? AccountName,
    string? Email,
    UserRole Role,
    UserStatus Status,
    AuthMethod AuthMethod,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt)
{
    public static AdminUserResponse From(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new(user.Id, user.DisplayName, user.AccountName, user.Email, user.Role, user.Status, MeResponse.AuthMethodOf(user), user.LastLoginAt, user.CreatedAt);
    }
}
