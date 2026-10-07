using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Ymir.VibeMaker.Application.Connectors.OneDrive;

namespace Ymir.VibeMaker.Infrastructure.Connectors.OneDrive;

/// <summary>
/// Entra v2 的授權碼 + PKCE 與 refresh token 換發（ADR-0013 §1、§2）。client secret 以 form 參數送出，
/// 錯誤只記錄 Entra 的 error code，不記錄回應內容（可能含 token）。
/// </summary>
internal sealed partial class OneDriveOAuthClient(HttpClient http, TimeProvider timeProvider, ILogger<OneDriveOAuthClient> logger) : IOneDriveOAuthClient
{
    public const string HttpClientName = "Ymir.OneDrive.OAuth";

    public Uri BuildAuthorizeUri(OneDriveOAuthSettings settings, Uri redirectUri, string state, string codeChallenge, string? loginHint)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(redirectUri);
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = settings.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri.ToString(),
            ["response_mode"] = "query",
            ["scope"] = OneDriveOAuthSettings.Scope,
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        };
        if (!string.IsNullOrWhiteSpace(loginHint))
        {
            query["login_hint"] = loginHint;
        }

        return new Uri(QueryHelpers.AddQueryString(settings.Endpoint("authorize").ToString(), query));
    }

    public Task<OneDriveTokens> RedeemCodeAsync(OneDriveOAuthSettings settings, string code, Uri redirectUri, string codeVerifier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(redirectUri);
        return RequestTokenAsync(settings, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri.ToString(),
            ["code_verifier"] = codeVerifier,
        }, cancellationToken);
    }

    public Task<OneDriveTokens> RefreshAsync(OneDriveOAuthSettings settings, string refreshToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return RequestTokenAsync(settings, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["scope"] = OneDriveOAuthSettings.Scope,
        }, cancellationToken);
    }

    private async Task<OneDriveTokens> RequestTokenAsync(OneDriveOAuthSettings settings, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        form["client_id"] = settings.ClientId;
        form["client_secret"] = settings.ClientSecret;
        HttpResponseMessage response;
        try
        {
            using var content = new FormUrlEncodedContent(form);
            response = await http.PostAsync(settings.Endpoint("token"), content, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new OneDriveException("暫時無法連線到 Microsoft 登入服務，請稍後再試。", ex);
        }

        using (response)
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var root = document.RootElement;
            if (!response.IsSuccessStatusCode)
            {
                var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
                LogTokenError(logger, (int)response.StatusCode, error);
                if (error is "invalid_grant" || response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw new OneDriveAuthorizationException("OneDrive 授權已失效，請重新連結。");
                }

                throw new OneDriveException(error is "consent_required" or "invalid_scope"
                    ? "公司的 Entra 設定尚未允許 Ymir 存取 OneDrive，請洽管理員。"
                    : "無法取得 OneDrive 授權，請稍後再試。");
            }

            var accessToken = root.GetProperty("access_token").GetString() ?? throw new OneDriveException("無法取得 OneDrive 授權，請稍後再試。");
            var refreshToken = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
            var expiresIn = root.TryGetProperty("expires_in", out var x) && x.TryGetInt32(out var seconds) ? seconds : 3600;
            return new OneDriveTokens(accessToken, refreshToken, timeProvider.GetUtcNow().AddSeconds(expiresIn));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Entra token endpoint returned {StatusCode} ({Error}) for the OneDrive connector")]
    private static partial void LogTokenError(ILogger logger, int statusCode, string? error);
}
