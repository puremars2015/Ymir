using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ymir.Api.Endpoints;

namespace Ymir.IntegrationTests.Api;

public class AuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    [Fact]
    public async Task Me_WithoutLogin_Returns401ProblemWithCode()
    {
        using var client = factory.CreateBrowserClient();
        var response = await client.GetAsync(new Uri("/api/me", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("AUTH_REQUIRED", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task DevLogin_SetsHttpOnlyCookie_AndMeReturnsSameUserOnRelogin()
    {
        var ct = TestContext.Current.CancellationToken;
        using var first = await factory.LoginAsync("alice");
        var me1 = await first.GetFromJsonAsync<MeResponse>("/api/me", Json, ct);

        using var second = await factory.LoginAsync("alice");
        var me2 = await second.GetFromJsonAsync<MeResponse>("/api/me", Json, ct);

        Assert.NotNull(me1);
        Assert.Equal(me1.Id, me2!.Id);
        Assert.Equal("alice", me1.AccountName);
    }

    [Fact]
    public async Task AuthCookie_IsHttpOnly()
    {
        using var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/dev/login", new DevLoginRequest("bob", null, null), TestContext.Current.CancellationToken);

        var authCookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("ymir.auth=", StringComparison.Ordinal));
        Assert.Contains("httponly", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", authCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostWithoutXsrfHeader_IsRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.CreateBrowserClient(sendXsrfHeader: false);
        (await client.PostAsJsonAsync("/api/dev/login", new DevLoginRequest("carol", null, null), ct)).EnsureSuccessStatusCode();

        var response = await client.PostAsync(new Uri("/api/auth/logout", UriKind.Relative), null, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("ANTIFORGERY_INVALID", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Logout_EndsSession()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await factory.LoginAsync("dave");

        var logout = await client.PostAsync(new Uri("/api/auth/logout", UriKind.Relative), null, ct);
        var me = await client.GetAsync(new Uri("/api/me", UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task DevLogin_RejectsEmptyAccount()
    {
        using var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/dev/login", new DevLoginRequest("  ", null, null), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
