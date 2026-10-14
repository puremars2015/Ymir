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

/// <summary>API + Fake OIDC 的 Google 模擬：Google 帳號 SSO 與白名單（ADR-0023）。</summary>
public sealed class GoogleApiFactory : ApiFactory
{
    public const string ClientId = "ymir-google-integration";
    public const string ClientSecret = "google-integration-secret";

    private readonly Lazy<FakeOidcServer> _oidc = new(() =>
        FakeOidcServer.StartAsync(new FakeOidcSettings(ClientId, ClientSecret)).GetAwaiter().GetResult());

    public FakeOidcServer Oidc => _oidc.Value;

    protected override void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting("Ymir:Auth:Google:Authority", Oidc.GoogleAuthority);
        builder.UseSetting("Ymir:Auth:Google:ClientId", ClientId);
        builder.UseSetting("Ymir:Auth:Google:ClientSecret", ClientSecret);
        builder.UseSetting("Ymir:Auth:Google:AllowedDomains:0", "acme.test");
        builder.UseSetting("Ymir:Auth:Google:AllowedEmails:0", "Partner@gmail.com");
        builder.UseSetting("Ymir:Auth:Google:AdminEmails:0", "boss@acme.test");
    }

    /// <summary>Ymir → Fake Google（<c>login_hint</c> 直接通過）→ Ymir callback；回傳 client 與最後導向的位址。</summary>
    public async Task<(HttpClient Client, string FinalLocation)> LoginWithGoogleAsync(string hint, string returnUrl = "/")
    {
        var ct = TestContext.Current.CancellationToken;
        var browser = this.CreateBrowserClient();
        using var challenge = await browser.GetAsync(new Uri($"/api/auth/login/google?returnUrl={Uri.EscapeDataString(returnUrl)}", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var authorizeUrl = challenge.Headers.Location!;
        Assert.StartsWith(Oidc.GoogleAuthority, authorizeUrl.ToString(), StringComparison.Ordinal);

        using var idp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var authorized = await idp.GetAsync(new Uri(QueryHelpers.AddQueryString(authorizeUrl.ToString(), "login_hint", hint)), ct);
        Assert.Equal(HttpStatusCode.Redirect, authorized.StatusCode);
        var callback = authorized.Headers.Location!;
        Assert.Equal("/signin-google", callback.AbsolutePath);

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

public class GoogleLoginTests(GoogleApiFactory factory) : IClassFixture<GoogleApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task Providers_ShowGoogle()
    {
        using var anonymous = factory.CreateBrowserClient();

        var providers = await anonymous.GetFromJsonAsync<LoginProvidersResponse>("/api/auth/providers", Json, TestContext.Current.CancellationToken);

        Assert.True(providers!.Google);
        Assert.Equal("Google 帳號", providers.GoogleDisplayName);
        Assert.False(providers.Oidc);
    }

    [Fact]
    public async Task WorkspaceAccount_InAllowedDomain_SignsIn_AndRedirectsBack()
    {
        var ct = TestContext.Current.CancellationToken;

        var (client, location) = await factory.LoginWithGoogleAsync("alice@acme.test", returnUrl: "/c/abc");
        using var _ = client;
        var me = await client.GetFromJsonAsync<MeResponse>("/api/me", Json, ct);

        Assert.Equal("/c/abc", location);
        Assert.Equal("alice@acme.test", me!.Email);
        Assert.Equal("alice@acme.test", me.AccountName);
        Assert.Equal(AuthMethod.Google, me.AuthMethod);
        Assert.Equal(UserRole.User, me.Role);
    }

    [Fact]
    public async Task SameGoogleAccount_IsSameUser_AndAdminEmailIsAdmin()
    {
        var ct = TestContext.Current.CancellationToken;

        var (first, _) = await factory.LoginWithGoogleAsync("boss@acme.test");
        using var a = first;
        var (second, _) = await factory.LoginWithGoogleAsync("BOSS@acme.test");
        using var b = second;
        var me1 = await first.GetFromJsonAsync<MeResponse>("/api/me", Json, ct);
        var me2 = await second.GetFromJsonAsync<MeResponse>("/api/me", Json, ct);

        Assert.Equal(me1!.Id, me2!.Id);
        Assert.Equal(UserRole.Admin, me1.Role);
        using var adminApi = await first.GetAsync(new Uri("/api/admin/users", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.OK, adminApi.StatusCode);
    }

    [Fact]
    public async Task IndividuallyAllowedGmail_SignsIn()
    {
        var (client, location) = await factory.LoginWithGoogleAsync("partner@gmail.com");
        using var _ = client;

        Assert.Equal("/", location);
    }

    [Theory]
    [InlineData("stranger@gmail.com")]
    [InlineData("eve@evil.test")]
    [InlineData("alice@acme.test" + FakeOidcEndpoints.UnverifiedHintSuffix)]
    [InlineData("partner@gmail.com" + FakeOidcEndpoints.UnverifiedHintSuffix)]
    public async Task AccountOutsideAllowList_OrUnverifiedEmail_IsRejected_WithoutCreatingUser(string hint)
    {
        var ct = TestContext.Current.CancellationToken;

        var (client, location) = await factory.LoginWithGoogleAsync(hint);
        using var _ = client;

        Assert.Equal("/login?error=not_allowed", location);
        using var me = await client.GetAsync(new Uri("/api/me", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Theory]
    [InlineData("https://evil.example/steal")]
    [InlineData("//evil.example/steal")]
    public async Task GoogleLogin_DoesNotRedirectOffSite(string returnUrl)
    {
        var (client, location) = await factory.LoginWithGoogleAsync("redirect@acme.test", returnUrl: returnUrl);
        using var _ = client;

        Assert.Equal("/", location);
    }

    [Fact]
    public async Task DisabledGoogleUser_CannotSignIn()
    {
        var ct = TestContext.Current.CancellationToken;
        var (victim, _) = await factory.LoginWithGoogleAsync("carol@acme.test");
        using var v = victim;
        var me = await victim.GetFromJsonAsync<MeResponse>("/api/me", Json, ct);
        var (admin, _) = await factory.LoginWithGoogleAsync("boss@acme.test");
        using var a = admin;
        await admin.GetAsync(new Uri("/api/me", UriKind.Relative), ct);

        using var disable = await admin.PostAsync(new Uri($"/api/admin/users/{me!.Id}/disable", UriKind.Relative), null, ct);
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);

        using var afterDisable = await victim.GetAsync(new Uri("/api/me", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.Unauthorized, afterDisable.StatusCode);
        var (_, location) = await factory.LoginWithGoogleAsync("carol@acme.test");
        Assert.Equal("/login?error=disabled", location);
    }

    [Fact]
    public async Task Callback_WithForgedState_IsRejected()
    {
        using var browser = factory.CreateBrowserClient();

        using var response = await browser.GetAsync(new Uri("/signin-google?code=forged&state=forged", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login?error=failed", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Login_WhenGoogleIsNotConfigured_Returns404()
    {
        await using var plain = new ApiFactory();
        using var browser = plain.CreateBrowserClient();

        using var response = await browser.GetAsync(new Uri("/api/auth/login/google", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
