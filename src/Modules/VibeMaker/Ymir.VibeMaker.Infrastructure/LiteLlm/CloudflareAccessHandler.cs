using Microsoft.Extensions.Options;

namespace Ymir.VibeMaker.Infrastructure.LiteLlm;

/// <summary>
/// LiteLLM 的管理端點放在 Cloudflare Access 後面時（ADR-0004 補充），為 API 送往 LiteLLM 的請求附加 service token。
/// 只附加在與 <c>VibeMaker:LiteLlm:BaseUrl</c> 同一來源（scheme + host + port）的請求：知識庫可能設定別的位址，
/// token 不得送到其他主機。這些 client 一律不跟隨 redirect（見 <see cref="CreatePrimaryHandler"/>），
/// 因為 .NET 跟隨 redirect 時只移除 Authorization，自訂 header 會被帶到新的位址。
/// </summary>
internal sealed class CloudflareAccessHandler(IOptions<LiteLlmOptions> options) : DelegatingHandler
{
    public const string ClientIdHeader = "CF-Access-Client-Id";
    public const string ClientSecretHeader = "CF-Access-Client-Secret";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (settings.HasCloudflareAccess && settings.BaseUrl is not null && request.RequestUri is { IsAbsoluteUri: true } target
            && Uri.Compare(target, settings.BaseUrl, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0)
        {
            request.Headers.Remove(ClientIdHeader);
            request.Headers.Remove(ClientSecretHeader);
            request.Headers.TryAddWithoutValidation(ClientIdHeader, settings.CfAccessClientId);
            request.Headers.TryAddWithoutValidation(ClientSecretHeader, settings.CfAccessClientSecret);
        }

        return base.SendAsync(request, cancellationToken);
    }

    /// <summary>LiteLLM 的 API 不需要 redirect；Access 設定錯誤時的 302（導向登入頁）應視為失敗，而不是跟過去。</summary>
    public static HttpMessageHandler CreatePrimaryHandler() => new SocketsHttpHandler { AllowAutoRedirect = false };
}
