using Microsoft.Extensions.Options;
using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.Platform.Auditing;
using Ymir.Platform.Identity;
using Ymir.Platform.Settings;
using Ymir.Platform.Users;

namespace Ymir.Api.Endpoints;

/// <summary>
/// Admin 系統設定（ADR-0010）：企業帳號（Entra ID）登入。
/// client secret 只能寫入、不回傳；存檔後不必重啟，下一個登入請求就用新設定。
/// </summary>
internal static class AdminSettingsEndpoints
{
    public static IEndpointRouteBuilder MapAdminSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/settings").WithTags("Admin").RequireAuthorization(AuthSetup.AdminPolicy).RequireAntiforgeryHeader();

        group.MapGet("/oidc", GetOidcAsync).WithName("AdminGetOidcSettings").Produces<OidcSettingsResponse>();
        group.MapPut("/oidc", SaveOidcAsync).WithName("AdminSaveOidcSettings").Produces<OidcSettingsResponse>();
        group.MapDelete("/oidc", ResetOidcAsync).WithName("AdminResetOidcSettings").Produces<OidcSettingsResponse>();
        group.MapPost("/oidc/test", TestOidcAsync).WithName("AdminTestOidcSettings").Produces<OidcTestResponse>();
        return endpoints;
    }

    private static async Task<OidcSettingsResponse> GetOidcAsync(HttpContext context, OidcSettingsProvider oidc, IUserDirectory users, CancellationToken cancellationToken) =>
        await ToResponseAsync(context, await oidc.RefreshAsync(cancellationToken), users, cancellationToken);

    private static async Task<IResult> SaveOidcAsync(
        SaveOidcSettingsRequest request,
        HttpContext context,
        OidcSettingsProvider oidc,
        ISystemSettingsStore store,
        IUserDirectory users,
        IOptions<YmirAuthOptions> deployment,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var secret = string.IsNullOrEmpty(request.ClientSecret) ? null : request.ClientSecret;
        if (OidcSettingsKeys.Validate(request.TenantId, request.ClientId?.Trim(), request.AdminRole?.Trim(), request.DisplayName, secret) is { } problem)
        {
            return ApiProblem.Create(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", problem);
        }

        var current = await oidc.RefreshAsync(cancellationToken);
        if (request.Enabled && secret is null && current.SecretSource == OidcSettingsSource.None)
        {
            return ApiProblem.Create(StatusCodes.Status400BadRequest, "CLIENT_SECRET_REQUIRED", "請輸入 client secret。");
        }

        if (!request.Enabled && await LockoutProblemAsync(deployment.Value, users, cancellationToken) is { } lockout)
        {
            return lockout;
        }

        var document = new OidcSettingsDocument(
            request.Enabled,
            Guid.Parse(request.TenantId!).ToString("D"),
            request.ClientId!.Trim(),
            request.AdminRole!.Trim(),
            request.DisplayName!.Trim(),
            request.SecretExpiresOn);
        await store.SetAsync(OidcSettingsKeys.Document, document.ToJson(), currentUser.ActorName, cancellationToken);
        if (secret is not null)
        {
            await store.SetSecretAsync(OidcSettingsKeys.ClientSecret, secret, currentUser.ActorName, cancellationToken);
        }

        var updated = await oidc.RefreshAsync(cancellationToken);
        await auditLog.WriteAsync(
            new AuditEntry(currentUser.ActorName, secret is null ? "admin.settings.oidc.update" : "admin.settings.oidc.update_secret", "setting", OidcSettingsKeys.Document, AuditResult.Success, timeProvider.GetUtcNow(), null),
            cancellationToken);
        return TypedResults.Ok(await ToResponseAsync(context, updated, users, cancellationToken));
    }

    private static async Task<IResult> ResetOidcAsync(
        HttpContext context,
        OidcSettingsProvider oidc,
        ISystemSettingsStore store,
        IUserDirectory users,
        IOptions<YmirAuthOptions> deployment,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!deployment.Value.Oidc.IsConfigured && await LockoutProblemAsync(deployment.Value, users, cancellationToken) is { } lockout)
        {
            return lockout;
        }

        await store.DeleteAsync([OidcSettingsKeys.Document, OidcSettingsKeys.ClientSecret], cancellationToken);
        var updated = await oidc.RefreshAsync(cancellationToken);
        await auditLog.WriteAsync(
            new AuditEntry(currentUser.ActorName, "admin.settings.oidc.reset", "setting", OidcSettingsKeys.Document, AuditResult.Success, timeProvider.GetUtcNow(), null),
            cancellationToken);
        return TypedResults.Ok(await ToResponseAsync(context, updated, users, cancellationToken));
    }

    private static async Task<OidcTestResponse> TestOidcAsync(TestOidcSettingsRequest request, OidcSettingsTester tester, CancellationToken cancellationToken)
    {
        var result = await tester.TestAsync(request.TenantId ?? string.Empty, cancellationToken);
        return new OidcTestResponse(result.Ok, result.Message);
    }

    /// <summary>
    /// 停用企業帳號登入後必須還有其他方式能進來管理（ADR-0010）：本機帳號登入開啟，且至少有一個啟用中的本機 Admin。
    /// </summary>
    private static async Task<IResult?> LockoutProblemAsync(YmirAuthOptions deployment, IUserDirectory users, CancellationToken cancellationToken) =>
        !deployment.LocalAccounts.Enabled || !await users.HasActiveLocalAdminAsync(cancellationToken)
            ? ApiProblem.Create(
                StatusCodes.Status409Conflict,
                "LAST_LOGIN_METHOD",
                "停用後將沒有管理員能登入：請先建立一個啟用中的本機 Admin 帳號（使用者管理 → 新增本機帳號）。")
            : null;

    private static async Task<OidcSettingsResponse> ToResponseAsync(HttpContext context, EffectiveOidcSettings settings, IUserDirectory users, CancellationToken cancellationToken)
    {
        var updatedByName = settings.UpdatedBy is { } actor && AuditActor.TryGetUserId(actor) is { } userId
            ? (await users.FindAsync(userId, cancellationToken))?.DisplayName
            : null;
        return new OidcSettingsResponse(
            settings.Source,
            settings.Enabled,
            settings.IsConfigured,
            settings.TenantId,
            settings.ClientId,
            settings.SecretSource != OidcSettingsSource.None,
            settings.SecretSource,
            settings.SecretUpdatedAt,
            settings.SecretExpiresOn,
            settings.AdminRole,
            settings.DisplayName,
            $"{context.Request.Scheme}://{context.Request.Host}{OidcSignIn.CallbackPath}",
            settings.UpdatedAt,
            updatedByName);
    }
}

/// <param name="ClientSecret">留空表示不變更。</param>
public sealed record SaveOidcSettingsRequest(
    bool Enabled,
    string? TenantId,
    string? ClientId,
    string? ClientSecret,
    DateOnly? SecretExpiresOn,
    string? AdminRole,
    string? DisplayName);

public sealed record TestOidcSettingsRequest(string? TenantId);

public sealed record OidcTestResponse(bool Ok, string Message);

/// <summary>client secret 本身永遠不回傳，只有是否已設定、來源與時間。</summary>
public sealed record OidcSettingsResponse(
    OidcSettingsSource Source,
    bool Enabled,
    bool Configured,
    string? TenantId,
    string? ClientId,
    bool HasClientSecret,
    OidcSettingsSource SecretSource,
    DateTimeOffset? SecretUpdatedAt,
    DateOnly? SecretExpiresOn,
    string AdminRole,
    string DisplayName,
    string RedirectUri,
    DateTimeOffset? UpdatedAt,
    string? UpdatedByName);
