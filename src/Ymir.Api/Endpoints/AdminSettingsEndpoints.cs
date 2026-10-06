using Microsoft.Extensions.Options;
using Ymir.Api.Auth;
using Ymir.Api.Edge;
using Ymir.Api.Problems;
using Ymir.Edge;
using Ymir.Platform.Auditing;
using Ymir.Platform.Identity;
using Ymir.Platform.Settings;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Infrastructure.Runtime.Remote;

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

        group.MapGet("/tunnel", GetTunnelAsync).WithName("AdminGetTunnelSettings").Produces<TunnelSettingsResponse>();
        group.MapPut("/tunnel/token", SetTunnelTokenAsync).WithName("AdminSetTunnelToken").Produces<TunnelSettingsResponse>();
        group.MapPut("/tunnel/hostname", SetPublicHostnameAsync).WithName("AdminSetPublicHostname").Produces<TunnelSettingsResponse>();
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

    private static async Task<TunnelSettingsResponse> GetTunnelAsync(
        ITunnelManagement tunnel,
        PublicHostnameSettings hostnames,
        PublicEdgeOptions edge,
        CancellationToken cancellationToken) =>
        await ToTunnelResponseAsync(tunnel, edge, await hostnames.RefreshAsync(cancellationToken), cancellationToken);

    /// <summary>
    /// Tunnel token 只轉送給主機上的 runtime host（ADR-0010）：API 不保存、不寫入資料庫或 log，回應與稽核也不包含。
    /// </summary>
    private static async Task<IResult> SetTunnelTokenAsync(
        SetTunnelTokenRequest request,
        ITunnelManagement tunnel,
        PublicHostnameSettings hostnames,
        PublicEdgeOptions edge,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var token = request.Token?.Trim() ?? string.Empty;
        var outcome = await tunnel.SetTokenAsync(token, cancellationToken);
        switch (outcome)
        {
            case TunnelTokenOutcome.Unavailable:
                return ApiProblem.Create(StatusCodes.Status409Conflict, "TUNNEL_MANAGEMENT_UNAVAILABLE", "這個部署沒有開放由網頁管理 Cloudflare Tunnel，請依部署指南在主機上設定。");
            case TunnelTokenOutcome.Invalid:
                return ApiProblem.Create(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", "Tunnel token 格式不正確，請從 Cloudflare dashboard 重新複製。");
            case TunnelTokenOutcome.Failed:
                await auditLog.WriteAsync(new AuditEntry(currentUser.ActorName, "admin.settings.tunnel.token", "setting", "tunnel.token", AuditResult.Failure, timeProvider.GetUtcNow(), null), cancellationToken);
                return ApiProblem.Create(StatusCodes.Status502BadGateway, "TUNNEL_APPLY_FAILED", "已儲存 token，但重新啟動 Cloudflare Tunnel 失敗，請查看主機的 runtime host 記錄。");
        }

        await auditLog.WriteAsync(new AuditEntry(currentUser.ActorName, "admin.settings.tunnel.token", "setting", "tunnel.token", AuditResult.Success, timeProvider.GetUtcNow(), null), cancellationToken);
        return TypedResults.Ok(await ToTunnelResponseAsync(tunnel, edge, await hostnames.GetAsync(cancellationToken), cancellationToken));
    }

    private static async Task<IResult> SetPublicHostnameAsync(
        SetPublicHostnameRequest request,
        ITunnelManagement tunnel,
        PublicHostnameSettings hostnames,
        PublicEdgeOptions edge,
        ISystemSettingsStore store,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var hostname = request.Hostname?.Trim().ToLowerInvariant() ?? string.Empty;
        if (hostname.Length == 0)
        {
            await store.DeleteAsync([PublicHostnameSettings.Key], cancellationToken);
        }
        else if (!PublicEdgeHostnames.IsValid(hostname))
        {
            return ApiProblem.Create(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", "請輸入網域名稱，例如 ymir.example.com（不含 https:// 與路徑）。");
        }
        else
        {
            await store.SetAsync(PublicHostnameSettings.Key, hostname, currentUser.ActorName, cancellationToken);
        }

        var current = await hostnames.RefreshAsync(cancellationToken);
        await auditLog.WriteAsync(
            new AuditEntry(currentUser.ActorName, hostname.Length == 0 ? "admin.settings.tunnel.hostname_reset" : "admin.settings.tunnel.hostname", "setting", PublicHostnameSettings.Key, AuditResult.Success, timeProvider.GetUtcNow(), null),
            cancellationToken);
        return TypedResults.Ok(await ToTunnelResponseAsync(tunnel, edge, current, cancellationToken));
    }

    private static async Task<TunnelSettingsResponse> ToTunnelResponseAsync(
        ITunnelManagement tunnel,
        PublicEdgeOptions edge,
        SystemSettingValue? hostnameOverride,
        CancellationToken cancellationToken)
    {
        var state = await tunnel.GetStatusAsync(cancellationToken);
        var hostname = hostnameOverride?.Value ?? (string.IsNullOrWhiteSpace(edge.PublicHostname) ? null : edge.PublicHostname.Trim());
        var source = hostnameOverride is not null ? OidcSettingsSource.Database : hostname is null ? OidcSettingsSource.None : OidcSettingsSource.Deployment;
        return new TunnelSettingsResponse(
            edge.Enabled,
            hostname,
            source,
            state.ManagementAvailable,
            state.Configured,
            state.Active,
            state.UpdatedAt,
            hostname is null ? null : $"https://{hostname}{OidcSignIn.CallbackPath}");
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

public sealed record SetTunnelTokenRequest(string? Token);

/// <param name="Hostname">空字串表示還原為部署設定。</param>
public sealed record SetPublicHostnameRequest(string? Hostname);

/// <param name="PublicEdgeEnabled">部署設定是否開啟對外公開（<c>Ymir:PublicEdge:Enabled</c>，只能在部署設定修改）。</param>
/// <param name="ManagementAvailable">這個部署能否由網頁設定 tunnel token。</param>
/// <param name="TokenUpdatedAt">runtime host 上 token 檔案的更新時間；token 本身永遠不回傳。</param>
public sealed record TunnelSettingsResponse(
    bool PublicEdgeEnabled,
    string? Hostname,
    OidcSettingsSource HostnameSource,
    bool ManagementAvailable,
    bool Configured,
    bool Active,
    DateTimeOffset? TokenUpdatedAt,
    string? RedirectUri);
