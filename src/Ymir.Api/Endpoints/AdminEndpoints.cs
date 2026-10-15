using Microsoft.Extensions.Options;
using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.Platform.Auditing;
using Ymir.Platform.Identity;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Application.Users;

namespace Ymir.Api.Endpoints;

/// <summary>
/// Admin：使用者管理（SA §4、ADR-0009、ADR-0027）。停用 / 啟用、建立本機帳號、重設本機帳號密碼、
/// 設定顯示名稱、變更角色（含訪客）、刪除帳號。
/// 企業帳號與 Google 帳號「是否為 Admin」以 IdP 為準（每次登入同步），這裡只能在使用者與訪客之間切換。
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
        group.MapPut("/{userId:guid}/display-name", RenameAsync).WithName("AdminRenameUser").Produces<AdminUserResponse>();
        group.MapPut("/{userId:guid}/role", ChangeRoleAsync).WithName("AdminChangeUserRole").Produces<AdminUserResponse>();
        group.MapDelete("/{userId:guid}", DeleteAsync).WithName("AdminDeleteUser").Produces(StatusCodes.Status204NoContent);
        return endpoints;
    }

    /// <summary>顯示名稱上限，與 <c>platform.users.display_name</c> 欄位一致。</summary>
    internal const int DisplayNameMaxLength = 200;

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

    private static async Task<IResult> RenameAsync(
        Guid userId,
        RenameUserRequest request,
        ICurrentUser currentUser,
        IUserDirectory users,
        IAuditLog auditLog,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var displayName = request.DisplayName?.Trim();
        if (string.IsNullOrEmpty(displayName) || displayName.Length > DisplayNameMaxLength)
        {
            return ApiProblem.Create(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", $"顯示名稱必須是 1～{DisplayNameMaxLength} 個字元。");
        }

        var user = await users.RenameAsync(userId, displayName, cancellationToken);
        if (user is null)
        {
            return UserNotFound();
        }

        await auditLog.WriteAsync(new AuditEntry(currentUser.ActorName, "admin.user.rename", "user", userId.ToString("D"), AuditResult.Success, timeProvider.GetUtcNow(), null), cancellationToken);
        return TypedResults.Ok(AdminUserResponse.From(user));
    }

    private static async Task<IResult> ChangeRoleAsync(
        Guid userId,
        ChangeUserRoleRequest request,
        ICurrentUser currentUser,
        IUserDirectory users,
        IAuditLog auditLog,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (request.Role is not { } role || !Enum.IsDefined(role))
        {
            return ApiProblem.Create(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", "請選擇角色。");
        }

        if (userId == currentUser.UserId)
        {
            // 避免管理員把自己降級後失去管理介面。
            return ApiProblem.Create(StatusCodes.Status400BadRequest, "CANNOT_CHANGE_OWN_ROLE", "不能變更自己的角色。");
        }

        var existing = await users.FindAsync(userId, cancellationToken);
        if (existing is null || existing.Status == UserStatus.Deleted)
        {
            return UserNotFound();
        }

        if (!CanChangeRole(existing, role))
        {
            return ApiProblem.Create(StatusCodes.Status400BadRequest, "ROLE_MANAGED_BY_IDP", "企業帳號與 Google 帳號的管理員權限由公司的帳號系統設定，這裡只能在使用者與訪客之間切換。");
        }

        var user = await users.ChangeRoleAsync(userId, role, cancellationToken);
        if (user is null)
        {
            return UserNotFound();
        }

        // 下一個請求起生效：CookiePrincipalValidator 發現角色不同就換發 cookie。
        await auditLog.WriteAsync(new AuditEntry(currentUser.ActorName, $"admin.user.role.{role.ToString().ToLowerInvariant()}", "user", userId.ToString("D"), AuditResult.Success, timeProvider.GetUtcNow(), null), cancellationToken);
        return TypedResults.Ok(AdminUserResponse.From(user));
    }

    private static async Task<IResult> DeleteAsync(
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
            return ApiProblem.Create(StatusCodes.Status400BadRequest, "CANNOT_DELETE_SELF", "不能刪除自己的帳號。");
        }

        var user = await users.DeleteAsync(userId, cancellationToken);
        if (user is null)
        {
            return UserNotFound();
        }

        // 與停用相同：下一個請求就被擋下（CookiePrincipalValidator），再清理執行中的工作、金鑰與 runtime。
        await deactivation.DeactivateAsync(userId, currentUser.ActorName, cancellationToken);
        await auditLog.WriteAsync(new AuditEntry(currentUser.ActorName, "admin.user.delete", "user", userId.ToString("D"), AuditResult.Success, timeProvider.GetUtcNow(), null), cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// IdP 管理「是否為 Admin」的帳號（企業帳號、Google 帳號）：Ymir 不能授予或拿掉 Admin，否則下次登入就被覆寫（ADR-0027）。
    /// 本機帳號與開發登入的角色完全由 Ymir 管理。
    /// </summary>
    internal static bool CanChangeRole(User user, UserRole role)
    {
        ArgumentNullException.ThrowIfNull(user);
        var adminManagedByIdp = MeResponse.AuthMethodOf(user) is AuthMethod.Oidc or AuthMethod.Google;
        return !adminManagedByIdp || (role != UserRole.Admin && user.Role != UserRole.Admin);
    }

    private static IResult UserNotFound() => ApiProblem.Create(StatusCodes.Status404NotFound, "USER_NOT_FOUND", "找不到使用者。");
}

public sealed record CreateLocalUserRequest(string? Account, string? DisplayName, string? Email, UserRole? Role, string? InitialPassword);

public sealed record ResetPasswordRequest(string? NewPassword);

public sealed record RenameUserRequest(string? DisplayName);

public sealed record ChangeUserRoleRequest(UserRole? Role);

/// <param name="CanChangeAdminRole">
/// 能否在這裡授予或拿掉 Admin：企業帳號與 Google 帳號由 IdP 決定（ADR-0027），使用者與訪客之間一律可以切換。
/// </param>
public sealed record AdminUserResponse(
    Guid Id,
    string DisplayName,
    string? AccountName,
    string? Email,
    UserRole Role,
    UserStatus Status,
    AuthMethod AuthMethod,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    bool CanChangeAdminRole)
{
    public static AdminUserResponse From(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new(user.Id, user.DisplayName, user.AccountName, user.Email, user.Role, user.Status, MeResponse.AuthMethodOf(user), user.LastLoginAt, user.CreatedAt,
            AdminEndpoints.CanChangeRole(user, user.Role == UserRole.Admin ? UserRole.User : UserRole.Admin));
    }
}
