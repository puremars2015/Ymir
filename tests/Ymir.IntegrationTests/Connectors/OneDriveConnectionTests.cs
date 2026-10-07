using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.Auth;
using Ymir.Platform.Users;
using Ymir.Testing.FakeOidc;
using Ymir.VibeMaker.Application.Connectors.OneDrive;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Domain;

namespace Ymir.IntegrationTests.Connectors;

/// <summary>API + Fake OIDC（含 Fake Graph）：OneDrive connector 的連結流程（ADR-0013）。</summary>
public sealed class OneDriveApiFactory : ApiFactory
{
    private readonly Lazy<FakeOidcServer> _oidc = new(() =>
        FakeOidcServer.StartAsync(new FakeOidcSettings(OidcApiFactory.ClientId, OidcApiFactory.ClientSecret)).GetAwaiter().GetResult());

    public FakeOidcServer Oidc => _oidc.Value;

    protected override void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting("Ymir:Auth:Oidc:Authority", Oidc.Authority);
        builder.UseSetting("Ymir:Auth:Oidc:ClientId", OidcApiFactory.ClientId);
        builder.UseSetting("Ymir:Auth:Oidc:ClientSecret", OidcApiFactory.ClientSecret);
        builder.UseSetting("Ymir:Connectors:OneDrive:GraphBaseUrl", Oidc.GraphBaseUrl);
        // 排入工作時會立即喚醒 worker；縮短輪詢只是讓「使用者執行中先略過」的工作更快被處理。
        builder.UseSetting("Ymir:Connectors:OneDrive:PollInterval", "00:00:00.500");
    }

    /// <summary>以 Fake OIDC 登入企業帳號（同 <see cref="OidcApiFactory.LoginWithOidcAsync"/>）。</summary>
    public async Task<HttpClient> LoginWithOidcAsync(string account)
    {
        var ct = TestContext.Current.CancellationToken;
        var browser = this.CreateBrowserClient();
        using var challenge = await browser.GetAsync(new Uri("/api/auth/login?returnUrl=/", UriKind.Relative), ct);
        using var idp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var authorized = await idp.GetAsync(new Uri(QueryHelpers.AddQueryString(challenge.Headers.Location!.ToString(), "login_hint", account)), ct);
        using var signedIn = await browser.GetAsync(new Uri(authorized.Headers.Location!.PathAndQuery, UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        return browser;
    }

    /// <summary>連結 OneDrive：Ymir connect → Fake OIDC（以 <paramref name="microsoftAccount"/> 授權）→ Ymir callback；回傳最後導向的位址。</summary>
    public static async Task<string> ConnectOneDriveAsync(HttpClient client, string microsoftAccount, Func<string, string>? tamperCallback = null)
    {
        var ct = TestContext.Current.CancellationToken;
        using var connect = await client.GetAsync(new Uri("/api/connectors/onedrive/connect?returnUrl=/settings", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.Redirect, connect.StatusCode);
        var authorize = connect.Headers.Location!.ToString();
        Assert.Contains("Files.ReadWrite", Uri.UnescapeDataString(authorize), StringComparison.Ordinal);
        Assert.Contains("code_challenge_method=S256", authorize, StringComparison.Ordinal);

        using var idp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var authorized = await idp.GetAsync(new Uri(QueryHelpers.AddQueryString(authorize, "login_hint", microsoftAccount)), ct);
        Assert.Equal(HttpStatusCode.Redirect, authorized.StatusCode);
        var callback = authorized.Headers.Location!.PathAndQuery;
        using var completed = await client.GetAsync(new Uri(tamperCallback?.Invoke(callback) ?? callback, UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.Redirect, completed.StatusCode);
        return completed.Headers.Location!.ToString();
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

public class OneDriveConnectionTests(OneDriveApiFactory factory) : IClassFixture<OneDriveApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> UserIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/me", Ct)).GetProperty("id").GetGuid();

    private async Task AllowOneDriveAsync(Guid userId)
    {
        using var admin = await factory.LoginAsync($"od-admin-{Guid.NewGuid():N}", UserRole.Admin);
        using var response = await admin.PutAsJsonAsync(
            $"/api/admin/users/{userId}/extensions",
            new SaveUserExtensionsRequest(ExtensionGrantSetting.Inherit, ExtensionGrantSetting.Inherit, ExtensionGrantSetting.Inherit, ExtensionGrantSetting.Allow),
            JsonDefaults.Options,
            Ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<OneDriveConnection?> ConnectionAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IVibeMakerDbContext>().OneDriveConnections.AsNoTracking().SingleOrDefaultAsync(c => c.UserId == userId, Ct);
    }

    private static Task<OneDriveStatusResponse?> StatusAsync(HttpClient client) =>
        client.GetFromJsonAsync<OneDriveStatusResponse>("/api/connectors/onedrive", JsonDefaults.Options, Ct);

    [Fact]
    public async Task Connect_SetRoot_Disconnect_WithoutEverExposingTokens()
    {
        using var client = await factory.LoginWithOidcAsync("od-alice");
        var userId = await UserIdAsync(client);
        await AllowOneDriveAsync(userId);

        var final = await OneDriveApiFactory.ConnectOneDriveAsync(client, "od-alice");

        Assert.Equal("/settings?onedrive=connected", final);
        var statusBody = await client.GetStringAsync("/api/connectors/onedrive", Ct);
        var status = JsonSerializer.Deserialize<OneDriveStatusResponse>(statusBody, JsonDefaults.Options)!;
        Assert.Equal(OneDriveLinkState.Connected, status.State);
        Assert.Equal("od-alice@fake-entra.test", status.Account);
        Assert.Null(status.RootPath);
        // token 不出現在回應；資料庫只有加密後的 refresh token（ADR-0013 §2）。
        Assert.DoesNotContain("fake-rt-", statusBody, StringComparison.Ordinal);
        Assert.DoesNotContain("fake-at-", statusBody, StringComparison.Ordinal);
        var stored = await ConnectionAsync(userId);
        Assert.DoesNotContain("fake-rt-", stored!.ProtectedRefreshToken, StringComparison.Ordinal);
        Assert.Equal(FakeOidcIssuer.StableGuid("oid:od-alice").ToString("D"), stored.MicrosoftUserId);

        using var root = await client.PutAsJsonAsync("/api/connectors/onedrive/root", new SetOneDriveRootRequest(" Ymir / Work "), Ct);
        Assert.Equal(HttpStatusCode.OK, root.StatusCode);
        Assert.Equal("/Ymir/Work", (await root.Content.ReadFromJsonAsync<OneDriveStatusResponse>(JsonDefaults.Options, Ct))!.RootPath);
        Assert.True(factory.Oidc.Graph.DriveOf("od-alice").FolderExists("/Ymir/Work"));

        using var disconnect = await client.DeleteAsync(new Uri("/api/connectors/onedrive", UriKind.Relative), Ct);
        Assert.Equal(OneDriveLinkState.NotConnected, (await disconnect.Content.ReadFromJsonAsync<OneDriveStatusResponse>(JsonDefaults.Options, Ct))!.State);
        Assert.Null(await ConnectionAsync(userId));

        using var admin = await factory.LoginAsync("od-audit-admin", UserRole.Admin);
        var audit = await admin.GetFromJsonAsync<AuditLogPageResponse>($"/api/admin/audit?userId={userId}&action=connector.onedrive", JsonDefaults.Options, Ct);
        Assert.Equal(
            ["connector.onedrive.connect", "connector.onedrive.disconnect", "connector.onedrive.root.update"],
            audit!.Items.Select(i => i.Action).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task WithoutCapability_ConnectIsForbidden()
    {
        using var client = await factory.LoginWithOidcAsync("od-denied");

        using var connect = await client.GetAsync(new Uri("/api/connectors/onedrive/connect", UriKind.Relative), Ct);
        using var root = await client.PutAsJsonAsync("/api/connectors/onedrive/root", new SetOneDriveRootRequest("/Ymir"), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, connect.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, root.StatusCode);
        Assert.False((await StatusAsync(client))!.Allowed);
    }

    [Fact]
    public async Task CompanyAccount_CannotLinkSomeoneElsesOneDrive()
    {
        using var client = await factory.LoginWithOidcAsync("od-carol");
        await AllowOneDriveAsync(await UserIdAsync(client));

        var final = await OneDriveApiFactory.ConnectOneDriveAsync(client, "od-mallory");

        Assert.Equal("/settings?onedrive=mismatch", final);
        Assert.Equal(OneDriveLinkState.NotConnected, (await StatusAsync(client))!.State);
    }

    [Fact]
    public async Task TamperedState_IsRejected()
    {
        using var client = await factory.LoginWithOidcAsync("od-dave");
        await AllowOneDriveAsync(await UserIdAsync(client));

        var final = await OneDriveApiFactory.ConnectOneDriveAsync(client, "od-dave", callback =>
            QueryHelpers.AddQueryString(callback.Split('?')[0], new Dictionary<string, string?>
            {
                ["code"] = QueryHelpers.ParseQuery(new Uri("http://x" + callback).Query)["code"],
                ["state"] = "forged-state",
            }));

        Assert.Equal("/settings?onedrive=invalid", final);
        Assert.Equal(OneDriveLinkState.NotConnected, (await StatusAsync(client))!.State);
    }

    [Fact]
    public async Task RevokedRefreshToken_MarksNeedsReauth()
    {
        using var client = await factory.LoginWithOidcAsync("od-erin");
        var userId = await UserIdAsync(client);
        await AllowOneDriveAsync(userId);
        await OneDriveApiFactory.ConnectOneDriveAsync(client, "od-erin");

        // 模擬密碼變更：refresh token 被撤銷，且記憶體中的 access token 已過期。
        factory.Oidc.Issuer.RevokeRefreshTokens("od-erin");
        factory.Services.GetRequiredService<OneDriveAccessTokenCache>().Remove(userId);
        using var root = await client.PutAsJsonAsync("/api/connectors/onedrive/root", new SetOneDriveRootRequest("/Ymir"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, root.StatusCode);
        var status = await StatusAsync(client);
        Assert.Equal(OneDriveLinkState.NeedsReauth, status!.State);

        // 重新連結後恢復。
        Assert.Equal("/settings?onedrive=connected", await OneDriveApiFactory.ConnectOneDriveAsync(client, "od-erin"));
        Assert.Equal(OneDriveLinkState.Connected, (await StatusAsync(client))!.State);
    }

    [Fact]
    public async Task RefreshToken_IsRotated_AndGraphThrottlingIsRetried()
    {
        using var client = await factory.LoginWithOidcAsync("od-frank");
        var userId = await UserIdAsync(client);
        await AllowOneDriveAsync(userId);
        await OneDriveApiFactory.ConnectOneDriveAsync(client, "od-frank");
        var before = (await ConnectionAsync(userId))!.ProtectedRefreshToken;

        factory.Services.GetRequiredService<OneDriveAccessTokenCache>().Remove(userId);
        factory.Oidc.Graph.ThrottleNext(1);
        using var root = await client.PutAsJsonAsync("/api/connectors/onedrive/root", new SetOneDriveRootRequest("/Ymir"), Ct);

        Assert.Equal(HttpStatusCode.OK, root.StatusCode);
        Assert.NotEqual(before, (await ConnectionAsync(userId))!.ProtectedRefreshToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/a/../b")]
    [InlineData("/bad:name")]
    public async Task InvalidRoot_IsRejectedWithSummary(string path)
    {
        using var client = await factory.LoginWithOidcAsync("od-grace");
        await AllowOneDriveAsync(await UserIdAsync(client));
        await OneDriveApiFactory.ConnectOneDriveAsync(client, "od-grace");

        using var root = await client.PutAsJsonAsync("/api/connectors/onedrive/root", new SetOneDriveRootRequest(path), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, root.StatusCode);
    }
}
