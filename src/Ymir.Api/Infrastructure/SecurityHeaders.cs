namespace Ymir.Api.Infrastructure;

/// <summary>
/// 所有回應的安全標頭（Sprint 5 強化）：
/// <list type="bullet">
/// <item>CSP：只允許同源的 script（Angular build 不含 inline script，見 angular.json 的 <c>inlineCritical: false</c>）；
/// style 允許 inline，因為 Angular 元件樣式在執行期插入 &lt;style&gt;。即使 Markdown 或檔案預覽出現漏洞，也無法執行注入的腳本。</item>
/// <item>禁止被嵌入 iframe（frame-ancestors / X-Frame-Options）、不送完整 referrer、關閉用不到的瀏覽器功能。</item>
/// </list>
/// 端點自己設定的標頭優先（例如下載檔案的 CSP <c>sandbox</c>），這裡只補沒有設定的。
/// </summary>
internal static class SecurityHeaders
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; " +
        "font-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public const string PermissionsPolicy = "camera=(), microphone=(), geolocation=(), payment=(), usb=(), serial=(), bluetooth=()";

    public static WebApplication UseSecurityHeaders(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.TryAdd("Content-Security-Policy", ContentSecurityPolicy);
                headers.TryAdd("X-Content-Type-Options", "nosniff");
                // antiforgery 會先設 SAMEORIGIN；Ymir 不需要被任何頁面嵌入，統一為 DENY（與 frame-ancestors 'none' 一致）。
                headers.XFrameOptions = "DENY";
                headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
                headers.TryAdd("Permissions-Policy", PermissionsPolicy);
                headers.TryAdd("Cross-Origin-Opener-Policy", "same-origin");
                return Task.CompletedTask;
            });
            return next(context);
        });
        return app;
    }
}
