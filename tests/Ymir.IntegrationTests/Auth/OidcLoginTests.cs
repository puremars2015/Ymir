using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.Platform.Users;
using Ymir.Testing.FakeOidc;

namespace Ymir.IntegrationTests.Auth;

/// <summary>API + Fake OIDC（模擬 Entra ID）：企業帳號登入（ADR-0009）。</summary>
public sealed class OidcApiFactory : ApiFactory
{
    public const string ClientId = "ymir-integration";
    public const string ClientSecret = "integration-client-secret";

    private readonly Lazy<FakeOidcServer> _oidc = new(() =>
        FakeOidcServer.StartAsync(new FakeOidcSettings(ClientId, ClientSecret)).GetAwaiter().GetResult());

    public FakeOidcServer Oidc => _oidc.Value;

    protected override void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting("Ymir:Auth:Oidc:Authority", Oidc.Authority);
        builder.UseSetting("Ymir:Auth:Oidc:ClientId", ClientId);
        builder.UseSetting("Ymir:Auth:Oidc:ClientSecret", ClientSecret);
    }

    /// <summary>
    /// 跑完整的 Authorization Code 流程：Ymir → Fake OIDC（<c>login_hint</c> 直接通過）→ Ymir callback。
    /// 回傳已登入的 client 與 callback 最後導向的位址。
    /// </summary>
    public async Task<(HttpClient Client, string FinalLocation)> LoginWithOidcAsync(string account, bool admin = false, string returnUrl = "/")
    {
        var ct = TestContext.Current.CancellationToken;
        var browser = this.CreateBrowserClient();
        using var challenge = await browser.GetAsync(new Uri($"/api/auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var authorizeUrl = challenge.Headers.Location!;
        Assert.StartsWith(Oidc.Authority.Replace("/v2.0", string.Empty, StringComparison.Ordinal), authorizeUrl.ToString(), StringComparison.Ordinal);

        using var idp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        var hint = admin ? account + FakeOidcEndpoints.AdminHintSuffix : account;
        using var authorized = await idp.GetAsync(new Uri(QueryHelpers.AddQueryString(authorizeUrl.ToString(), "login_hint", hint)), ct);
        Assert.Equal(HttpStatusCode.Redirect, authorized.StatusCode);
        var callback = authorized.Headers.Location!;

        using var signedIn = await browser.GetAsync(new Uri(callback.PathAndQuery, UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        return (browser, signedIn.Headers.Location!.ToString());
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_oidc.IsValueCreated)
        {
            await _oidc.Value.DisposeAsync();
        }
    }
}

public class OidcLoginTests(OidcApiFactory factory) : IClassFixture<OidcApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task Providers_ShowOidcAndPasswordLogin()
    {
        using var anonymous = factory.CreateBrowserClient();

        var providers = await anonymous.GetFromJsonAsync<LoginProvidersResponse>("/api/auth/providers", Json, TestContext.Current.CancellationToken);

        Assert.True(providers!.Oidc);
        Assert.Equal("公司帳號", providers.OidcDisplayName);
        Assert.True(providers.Password);
    }

    [Fact]
    public async Task OidcLogin_CreatesUserFromEntraClaims_AndRedirectsBack()
    {
        var ct = TestContext.Current.CancellationToken;

        var (client, location) = await factory.LoginWithOidcAsync("oidc-alice", returnUrl: "/c/abc");
        using var _ = client;
        var me = await client.GetFromJsonAsync<MeResponse>("/api/me", Json, ct);

        Assert.Equal("/c/abc", location);
        Assert.Equal("oidc-alice@fake-entra.test", me!.AccountName);
        Assert.Equal("oidc-alice@fake-entra.test", me.Email);
        Assert.Equal(AuthMethod.Oidc, me.AuthMethod);
        Assert.Equal(UserRole.User, me.Role);
    }

    [Fact]
    public async Task OidcLogin_SameOid_IsSameUser_AndAppRoleIsSyncedOnEachLogin()
    {
        var ct = TestContext.Current.CancellationToken;

        var (asAdmin, _) = await factory.LoginWithOidcAsync("oidc-bob", admin: true);
        using var a = asAdmin;
        var first = await asAdmin.GetFromJsonAsync<MeResponse>("/api/me", Json, ct);
        // Entra 拿掉 Ymir.Admin 後再登入：降為 User。
        var (asUser, _) = await factory.LoginWithOidcAsync("oidc-bob");
        using var b = asUser;
        var second = await asUser.GetFromJsonAsync<MeResponse>("/api/me", Json, ct);

        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal(UserRole.Admin, first.Role);
        Assert.Equal(UserRole.User, second.Role);
        // 原本 Admin 的 cookie 也在下一個請求就降級（每請求檢查角色）。
        using var adminApi = await asAdmin.GetAsync(new Uri("/api/admin/users", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.Forbidden, adminApi.StatusCode);
    }

    [Theory]
    [InlineData("https://evil.example/steal")]
    [InlineData("//evil.example/steal")]
    [InlineData("/\\evil.example")]
    public async Task OidcLogin_DoesNotRedirectOffSite(string returnUrl)
    {
        var (client, location) = await factory.LoginWithOidcAsync("oidc-redirect", returnUrl: returnUrl);
        using var _ = client;

        Assert.Equal("/", location);
    }

    [Fact]
    public async Task Callback_WithForgedState_IsRejected()
    {
        using var browser = factory.CreateBrowserClient();

        using var response = await browser.GetAsync(new Uri("/signin-oidc?code=forged&state=forged", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login?error=failed", response.Headers.Location!.ToString());
        Assert.DoesNotContain(response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [], c => c.StartsWith("ymir.auth=", StringComparison.Ordinal) && !c.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DisabledUser_CannotSignInWithOidc_AndExistingCookieStopsWorking()
    {
        var ct = TestContext.Current.CancellationToken;
        var (victim, _) = await factory.LoginWithOidcAsync("oidc-carol");
        using var v = victim;
        var me = await victim.GetFromJsonAsync<MeResponse>("/api/me", Json, ct);
        var (admin, _) = await factory.LoginWithOidcAsync("oidc-admin", admin: true);
        using var a = admin;
        // 與前端相同：導回後先呼叫 /api/me，取得綁定登入使用者的 XSRF token。
        await admin.GetAsync(new Uri("/api/me", UriKind.Relative), ct);

        using var disable = await admin.PostAsync(new Uri($"/api/admin/users/{me!.Id}/disable", UriKind.Relative), null, ct);
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);

        using var afterDisable = await victim.GetAsync(new Uri("/api/me", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.Unauthorized, afterDisable.StatusCode);
        var (_, location) = await factory.LoginWithOidcAsync("oidc-carol");
        Assert.Equal("/login?error=disabled", location);
    }

    [Fact]
    public async Task Login_WhenOidcIsNotConfigured_Returns404()
    {
        await using var plain = new ApiFactory();
        using var browser = plain.CreateBrowserClient();

        using var response = await browser.GetAsync(new Uri("/api/auth/login", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
