using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Ymir.Api.Auth;

/// <summary>
/// 管理介面的「測試設定」（ADR-0010）：讀取 <c>{AuthorityHost}/{tenant}/v2.0/.well-known/openid-configuration</c>，
/// 確認 tenant 存在、issuer 相符。authority 由部署設定的固定 host 與 GUID 組成，無法被拿來連任意網址。
/// 回傳的只是摘要，不含遠端回應內容（SA §12）。
/// </summary>
public sealed partial class OidcSettingsTester(IHttpClientFactory httpClients, IOptions<YmirAuthOptions> options, ILogger<OidcSettingsTester> logger)
{
    public const string HttpClientName = "oidc-settings-test";

    public async Task<OidcTestResult> TestAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(tenantId, out var tenant))
        {
            return new OidcTestResult(false, "Tenant ID 必須是 GUID。");
        }

        var authority = OidcSettingsKeys.BuildAuthority(options.Value.Oidc.AuthorityHost, tenant.ToString("D"));
        try
        {
            var client = httpClients.CreateClient(HttpClientName);
            using var response = await client.GetAsync(new Uri($"{authority}/.well-known/openid-configuration"), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogTestFailed(logger, null, (int)response.StatusCode);
                return new OidcTestResult(false, $"找不到這個 tenant 的登入設定（HTTP {(int)response.StatusCode}），請確認 Tenant ID。");
            }

            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
            var issuer = document.RootElement.TryGetProperty("issuer", out var value) ? value.GetString() : null;
            return issuer is not null && issuer.Contains(tenant.ToString("D"), StringComparison.OrdinalIgnoreCase)
                ? new OidcTestResult(true, "已連線到 Entra ID，tenant 設定正確。Client ID 與 secret 會在實際登入時驗證。")
                : new OidcTestResult(false, "登入設定的 issuer 與 Tenant ID 不符。");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            LogTestFailed(logger, ex, 0);
            return new OidcTestResult(false, "無法連線到 Entra ID，請確認伺服器的網路連線。");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "OIDC settings test failed (HTTP {StatusCode})")]
    private static partial void LogTestFailed(ILogger logger, Exception? exception, int statusCode);
}

public sealed record OidcTestResult(bool Ok, string Message);
