using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.WebUtilities;
using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.Platform.Identity;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Application.Connectors.OneDrive;
using Ymir.VibeMaker.Domain;

namespace Ymir.Api.Endpoints;

/// <summary>
/// OneDrive connector（ADR-0013）：連結 / 解除連結、根資料夾、狀態。只作用在目前使用者自己身上；
/// 需要管理員開放 <c>onedrive</c> 能力（ADR-0012 擴充政策）。token 不出現在任何回應。
/// </summary>
internal static class OneDriveEndpoints
{
    public const string CallbackPath = "/api/connectors/onedrive/callback";
    public const string StateCookieName = "ymir.onedrive.oauth";
    private const string StatePurpose = "Ymir.Connectors.OneDrive.OAuthState.v1";
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    public static IEndpointRouteBuilder MapOneDriveEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/connectors/onedrive").WithTags("Connectors");
        group.MapGet("/", GetStatusAsync).WithName("GetOneDriveStatus").RequireAntiforgeryHeader();
        // connect / callback 是瀏覽器整頁導向（GET），無法帶 XSRF header；以只能用一次、綁定使用者的 state cookie 防 CSRF。
        group.MapGet("/connect", ConnectAsync).WithName("ConnectOneDrive").ExcludeFromDescription();
        group.MapGet("/callback", CallbackAsync).WithName("OneDriveCallback").ExcludeFromDescription();
        group.MapPut("/root", SetRootAsync).WithName("SetOneDriveRoot").RequireAntiforgeryHeader();
        group.MapDelete("/", DisconnectAsync).WithName("DisconnectOneDrive").RequireAntiforgeryHeader();

        // 對話（工作目錄）的雲端保存狀態與手動同步（ADR-0013 §4）：只能存取自己的對話（SA §12）。
        var conversations = endpoints.MapGroup("/api/conversations/{conversationId:guid}/onedrive").WithTags("Conversations").RequireAntiforgeryHeader();
        conversations.MapGet("/", GetConversationStatusAsync).WithName("GetConversationOneDriveStatus");
        conversations.MapPost("/sync", SyncConversationAsync).WithName("SyncConversationOneDrive");
        return endpoints;
    }

    private static async Task<Results<Ok<ConversationOneDriveResponse>, NotFound>> GetConversationStatusAsync(
        Guid conversationId,
        ICurrentUser currentUser,
        OneDriveSyncService sync,
        CancellationToken cancellationToken) =>
        await sync.GetStatusAsync(currentUser.UserId, conversationId, cancellationToken) is { } status
            ? TypedResults.Ok(ConversationOneDriveResponse.From(status))
            : TypedResults.NotFound();

    private static async Task<IResult> SyncConversationAsync(
        Guid conversationId,
        ICurrentUser currentUser,
        OneDriveSyncService sync,
        CancellationToken cancellationToken)
    {
        var status = await sync.RequestSyncAsync(currentUser.UserId, conversationId, cancellationToken);
        if (status is null)
        {
            return TypedResults.NotFound();
        }

        return status.Availability == OneDriveAvailability.Ready
            ? TypedResults.Accepted((string?)null, ConversationOneDriveResponse.From(status))
            : ApiProblem.Create(StatusCodes.Status409Conflict, "ONEDRIVE_NOT_READY", status.Availability switch
            {
                OneDriveAvailability.NotAllowed => "管理員尚未開放 OneDrive 連結。",
                OneDriveAvailability.NeedsReauth => "OneDrive 授權已失效，請到設定頁重新連結。",
                OneDriveAvailability.NoRoot => "請先到設定頁選擇 OneDrive 同步資料夾。",
                _ => "請先到設定頁連結 OneDrive。",
            });
    }

    private static async Task<OneDriveStatusResponse> GetStatusAsync(ICurrentUser currentUser, OneDriveConnectionService onedrive, CancellationToken cancellationToken) =>
        OneDriveStatusResponse.From(await onedrive.GetStatusAsync(currentUser.UserId, cancellationToken));

    private static async Task<IResult> ConnectAsync(
        string? returnUrl,
        HttpContext context,
        ICurrentUser currentUser,
        IUserDirectory users,
        OneDriveConnectionService onedrive,
        IDataProtectionProvider dataProtection,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!await onedrive.IsAllowedAsync(currentUser.UserId, cancellationToken))
        {
            return NotAllowed();
        }

        var state = Base64UrlTextEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var verifier = Base64UrlTextEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlTextEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var user = await users.FindAsync(currentUser.UserId, cancellationToken);
        var authorize = await onedrive.BuildAuthorizeUriAsync(RedirectUri(context), state, challenge, user?.Email ?? user?.AccountName, cancellationToken);
        if (authorize is null)
        {
            return ApiProblem.Create(StatusCodes.Status400BadRequest, "ONEDRIVE_UNAVAILABLE", "企業帳號登入尚未設定，無法連結 OneDrive。");
        }

        var payload = new OAuthState(state, verifier, currentUser.UserId, SafeRedirect.LocalPathOrRoot(returnUrl), timeProvider.GetUtcNow().Add(StateLifetime));
        context.Response.Cookies.Append(StateCookieName, Protector(dataProtection).Protect(JsonSerializer.Serialize(payload)), new CookieOptions
        {
            HttpOnly = true,
            // Microsoft 導回 callback 是跨站的頂層 GET 導覽：Lax 才會帶上 cookie。
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            Path = CallbackPath,
            MaxAge = StateLifetime,
        });
        return Results.Redirect(authorize.ToString());
    }

    private static async Task<IResult> CallbackAsync(
        string? code,
        string? state,
        string? error,
        HttpContext context,
        ICurrentUser currentUser,
        IUserDirectory users,
        IOidcSettingsSource oidc,
        OneDriveConnectionService onedrive,
        IDataProtectionProvider dataProtection,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var payload = ReadState(context, dataProtection);
        context.Response.Cookies.Delete(StateCookieName, new CookieOptions { Path = CallbackPath });
        // state 必須存在、相符、未過期，而且是同一位使用者發起的（避免把別人的 OneDrive 連進自己的帳號，反之亦然）。
        if (payload is null
            || state is null
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(payload.State), Encoding.UTF8.GetBytes(state))
            || payload.ExpiresAt < timeProvider.GetUtcNow()
            || payload.UserId != currentUser.UserId)
        {
            return Results.Redirect(WithResult("/settings", "invalid"));
        }

        if (error is not null || string.IsNullOrEmpty(code))
        {
            // 使用者在 Microsoft 頁面取消或租戶未同意：只帶摘要代碼回去，不轉送 IdP 的錯誤描述。
            return Results.Redirect(WithResult(payload.ReturnUrl, error == "access_denied" ? "denied" : "error"));
        }

        if (!await onedrive.IsAllowedAsync(currentUser.UserId, cancellationToken))
        {
            return Results.Redirect(WithResult(payload.ReturnUrl, "forbidden"));
        }

        var user = await users.FindAsync(currentUser.UserId, cancellationToken);
        // 企業帳號：連結的 Microsoft 帳號必須是同一人（oid）；本機帳號沒有可比對的 oid。
        var expectedOid = user is not null && MeResponse.AuthMethodOf(user) == AuthMethod.Oidc && oidc.Current.SubjectClaim == "oid" ? user.Subject : null;
        var result = await onedrive.CompleteConnectAsync(currentUser.UserId, expectedOid, code, RedirectUri(context), payload.Verifier, currentUser.ActorName, cancellationToken);
        return Results.Redirect(WithResult(payload.ReturnUrl, result switch
        {
            OneDriveConnectResult.Connected => "connected",
            OneDriveConnectResult.AccountMismatch => "mismatch",
            _ => "error",
        }));
    }

    private static async Task<IResult> SetRootAsync(
        SetOneDriveRootRequest request,
        ICurrentUser currentUser,
        OneDriveConnectionService onedrive,
        CancellationToken cancellationToken)
    {
        if (!await onedrive.IsAllowedAsync(currentUser.UserId, cancellationToken))
        {
            return NotAllowed();
        }

        if (await onedrive.SetRootAsync(currentUser.UserId, request.Path, currentUser.ActorName, cancellationToken) is { } problem)
        {
            return ApiProblem.Create(StatusCodes.Status400BadRequest, "ONEDRIVE_ROOT_INVALID", problem);
        }

        return TypedResults.Ok(OneDriveStatusResponse.From(await onedrive.GetStatusAsync(currentUser.UserId, cancellationToken)));
    }

    /// <summary>解除連結不需要能力：被關閉的使用者也可以移除自己的連結與 token。</summary>
    private static async Task<OneDriveStatusResponse> DisconnectAsync(ICurrentUser currentUser, OneDriveConnectionService onedrive, CancellationToken cancellationToken)
    {
        await onedrive.DisconnectAsync(currentUser.UserId, currentUser.ActorName, cancellationToken);
        return OneDriveStatusResponse.From(await onedrive.GetStatusAsync(currentUser.UserId, cancellationToken));
    }

    /// <summary>callback 位址由目前請求推導（經 Edge 時已套用可信任的 X-Forwarded-*），必須登記在 Entra 應用程式。</summary>
    private static Uri RedirectUri(HttpContext context) => new($"{context.Request.Scheme}://{context.Request.Host}{CallbackPath}");

    private static string WithResult(string returnUrl, string result) => QueryHelpers.AddQueryString(returnUrl, "onedrive", result);

    private static IDataProtector Protector(IDataProtectionProvider provider) => provider.CreateProtector(StatePurpose);

    private static OAuthState? ReadState(HttpContext context, IDataProtectionProvider dataProtection)
    {
        if (!context.Request.Cookies.TryGetValue(StateCookieName, out var value) || string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<OAuthState>(Protector(dataProtection).Unprotect(value));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            return null;
        }
    }

    private static IResult NotAllowed() => ApiProblem.Create(StatusCodes.Status403Forbidden, "ONEDRIVE_NOT_ALLOWED", "管理員尚未開放 OneDrive 連結。");

    private sealed record OAuthState(string State, string Verifier, Guid UserId, string ReturnUrl, DateTimeOffset ExpiresAt);
}

/// <summary>Api 層提供 OneDrive 連結用的 Entra 設定：沿用 Ymir 登入的應用程式註冊（ADR-0013 §1、ADR-0010）。</summary>
internal sealed class OidcOneDriveSettingsSource(IOidcSettingsSource oidc) : IOneDriveOAuthSettingsSource
{
    public Task<OneDriveOAuthSettings?> GetAsync(CancellationToken cancellationToken)
    {
        var current = oidc.Current;
        return Task.FromResult(current.IsConfigured
            ? new OneDriveOAuthSettings(current.Authority!, current.ClientId!, current.ClientSecret!)
            : null);
    }
}

public sealed record SetOneDriveRootRequest(string? Path);

/// <param name="Allowed">管理員是否開放。</param>
/// <param name="Available">是否已設定企業帳號登入（沒有時無法連結）。</param>
/// <param name="Account">連結的 Microsoft 帳號（UPN）。</param>
/// <param name="RootPath">同步的根資料夾；尚未設定時為 null。</param>
public sealed record OneDriveStatusResponse(
    bool Allowed,
    bool Available,
    OneDriveLinkState State,
    string? Account,
    string? RootPath,
    DateTimeOffset? ConnectedAt,
    string? LastError)
{
    internal static OneDriveStatusResponse From(OneDriveStatus status) =>
        new(status.Allowed, status.Available, status.State, status.Account, status.RootPath, status.ConnectedAt, status.LastError);
}

/// <summary>對話的雲端保存狀態（ADR-0013 §4）；沒有 token、item id 或 host 路徑。</summary>
/// <param name="Availability">OneDrive 是否可用；不是 <c>Ready</c> 時不同步。</param>
/// <param name="FolderPath">相對於同步根資料夾的位置；尚未同步過時為 null。</param>
/// <param name="State">尚未同步過時為 null。</param>
/// <param name="ConflictCount">最近一輪同步另存的衝突副本數。</param>
/// <param name="LastError">失敗原因或略過檔案的摘要。</param>
public sealed record ConversationOneDriveResponse(
    OneDriveAvailability Availability,
    string? RootPath,
    string? FolderPath,
    OneDriveSyncState? State,
    DateTimeOffset? LastSyncedAt,
    int ConflictCount,
    string? LastError)
{
    internal static ConversationOneDriveResponse From(ConversationOneDriveStatus status) =>
        new(status.Availability, status.RootPath, status.FolderPath, status.State, status.LastSyncedAt, status.ConflictCount, status.LastError);
}
