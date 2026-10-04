using System.Net;

namespace Ymir.IntegrationTests.Api;

/// <summary>
/// 模擬瀏覽器 + Angular HttpClient：保存 cookie，非 GET 請求自動把 <c>XSRF-TOKEN</c> cookie 放到 <c>X-XSRF-TOKEN</c> header。
/// </summary>
internal sealed class BrowserLikeHandler(bool sendXsrfHeader = true) : DelegatingHandler
{
    public CookieContainer Cookies { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var cookieHeader = Cookies.GetCookieHeader(uri);
        if (!string.IsNullOrEmpty(cookieHeader))
        {
            request.Headers.Remove("Cookie");
            request.Headers.Add("Cookie", cookieHeader);
        }

        if (sendXsrfHeader && request.Method != HttpMethod.Get && Cookies.GetCookies(uri)["XSRF-TOKEN"] is { } xsrf)
        {
            request.Headers.Add("X-XSRF-TOKEN", xsrf.Value);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var setCookie in setCookies)
            {
                Cookies.SetCookies(uri, setCookie);
            }
        }

        return response;
    }
}
