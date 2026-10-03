using System.Net.Http.Json;
using Ymir.Api.Endpoints;
using Ymir.Platform.Users;

namespace Ymir.IntegrationTests.Api;

internal static class ApiFactoryExtensions
{
    /// <summary>建立一個像瀏覽器的 client（未登入）。</summary>
    public static HttpClient CreateBrowserClient(this ApiFactory factory, bool sendXsrfHeader = true) =>
        factory.CreateDefaultClient(new Uri("http://localhost"), new BrowserLikeHandler(sendXsrfHeader));

    /// <summary>以 Dev 帳號登入，回傳已帶 cookie 的 client。</summary>
    public static async Task<HttpClient> LoginAsync(this ApiFactory factory, string account, UserRole role = UserRole.User)
    {
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/dev/login", new DevLoginRequest(account, null, role), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return client;
    }
}
