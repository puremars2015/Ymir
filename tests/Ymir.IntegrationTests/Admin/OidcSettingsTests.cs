using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ymir.Api.Auth;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.Auth;
using Ymir.Platform.Infrastructure.Persistence;
using Ymir.Platform.Users;
using Ymir.Testing.FakeOidc;

namespace Ymir.IntegrationTests.Admin;

/// <summary>部署設定沒有 Entra，只設定 authority host 指向 Fake OIDC；Entra 由管理介面設定（ADR-0010）。</summary>
public sealed class DatabaseOidcApiFactory : OidcApiFactory
{
    protected override void Configure(IWebHostBuilder builder) =>
        builder.UseSetting("Ymir:Auth:Oidc:AuthorityHost", Oidc.RootUrl.ToString());
}

/// <summary>管理介面的 Entra 設定：不重啟即生效、secret 加密且不回傳、防鎖死（ADR-0010）。</summary>
public class OidcSettingsTests(DatabaseOidcApiFactory factory) : IClassFixture<DatabaseOidcApiFactory>
{
    private const string Secret = "integration-client-secret";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()!;

    private SaveOidcSettingsRequest Request(bool enabled = true, string? clientId = null, string? secret = Secret) =>
        new(enabled, factory.Oidc.Settings.TenantId, clientId ?? OidcApiFactory.ClientId, secret, new DateOnly(2027, 1, 31), "Ymir.Admin", "測試公司帳號");

    private async Task<LoginProvidersResponse> ProvidersAsync()
    {
        using var anonymous = factory.CreateBrowserClient();
        return (await anonymous.GetFromJsonAsync<LoginProvidersResponse>("/api/auth/providers", JsonDefaults.Options, Ct))!;
    }

    [Fact]
    public async Task EntraSettings_TakeEffectWithoutRestart_AndTheSecretIsNeverReturned()
    {
        using var admin = await factory.LoginAsync($"oidc-admin-{Guid.NewGuid():N}", UserRole.Admin);
        var before = await admin.GetFromJsonAsync<OidcSettingsResponse>("/api/admin/settings/oidc", JsonDefaults.Options, Ct);
        Assert.False(before!.Configured);
        Assert.Equal(OidcSettingsSource.None, before.Source);
        Assert.False((await ProvidersAsync()).Oidc);
        Assert.Equal("http://localhost/signin-oidc", before.RedirectUri);

        var saved = await admin.PutAsJsonAsync("/api/admin/settings/oidc", Request(), Ct);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var body = await saved.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain(Secret, body, StringComparison.Ordinal);
        var settings = JsonSerializer.Deserialize<OidcSettingsResponse>(body, JsonDefaults.Options)!;
        Assert.True(settings.Configured);
        Assert.Equal(OidcSettingsSource.Database, settings.Source);
        Assert.True(settings.HasClientSecret);
        Assert.Equal(OidcSettingsSource.Database, settings.SecretSource);
        Assert.Equal(new DateOnly(2027, 1, 31), settings.SecretExpiresOn);
        Assert.NotNull(settings.UpdatedByName);
        var providers = await ProvidersAsync();
        Assert.True(providers.Oidc);
        Assert.Equal("測試公司帳號", providers.OidcDisplayName);

        // 企業帳號登入可以完成（Fake OIDC 驗證 client secret）
        var (client, location) = await factory.LoginWithOidcAsync($"dyn-{Guid.NewGuid():N}", admin: true);
        using (client)
        {
            Assert.Equal("/", location);
            var me = await client.GetFromJsonAsync<JsonElement>("/api/me", Ct);
            Assert.Equal("Admin", me.GetProperty("role").GetString());
        }

        // 資料庫裡是密文
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var stored = await db.SystemSettings.AsNoTracking().SingleAsync(s => s.Key == OidcSettingsKeys.ClientSecret, Ct);
            Assert.True(stored.IsSecret);
            Assert.DoesNotContain(Secret, stored.Value, StringComparison.Ordinal);
        }

        // 只改 Client ID、secret 留空：沿用原本的 secret，登入請求立即使用新的 Client ID
        var changed = await admin.PutAsJsonAsync("/api/admin/settings/oidc", Request(clientId: "another-client", secret: null), Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.True((await changed.Content.ReadFromJsonAsync<OidcSettingsResponse>(JsonDefaults.Options, Ct))!.HasClientSecret);
        using var browser = factory.CreateBrowserClient();
        using var challenge = await browser.GetAsync(new Uri("/api/auth/login", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        Assert.Contains("client_id=another-client", challenge.Headers.Location!.Query, StringComparison.Ordinal);

        // 稽核有紀錄，但不含 secret
        var audit = await admin.GetFromJsonAsync<AuditLogPageResponse>("/api/admin/audit?action=admin.settings.oidc", JsonDefaults.Options, Ct);
        Assert.Contains(audit!.Items, i => i.Action == "admin.settings.oidc.update_secret");
        Assert.Contains(audit.Items, i => i.Action == "admin.settings.oidc.update");

        // 還原（需要本機 Admin 才能讓企業帳號登入消失）
        await EnsureLocalAdminAsync(admin);
        var reset = await admin.DeleteAsync("/api/admin/settings/oidc", Ct);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.False((await ProvidersAsync()).Oidc);
    }

    [Fact]
    public async Task Disabling_WithoutALocalAdmin_IsRefused()
    {
        using var admin = await factory.LoginAsync($"oidc-lock-{Guid.NewGuid():N}", UserRole.Admin);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/admin/settings/oidc", Request(), Ct)).StatusCode);

        // 這個 fixture 的資料庫可能已經有其他測試建立的本機 Admin；先把它們停用
        await DisableLocalAdminsAsync();
        var refused = await admin.PutAsJsonAsync("/api/admin/settings/oidc", Request(enabled: false, secret: null), Ct);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("LAST_LOGIN_METHOD", await ProblemCodeAsync(refused));
        Assert.True((await ProvidersAsync()).Oidc);

        await EnsureLocalAdminAsync(admin);
        var disabled = await admin.PutAsJsonAsync("/api/admin/settings/oidc", Request(enabled: false, secret: null), Ct);
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        Assert.False((await ProvidersAsync()).Oidc);
        using var browser = factory.CreateBrowserClient();
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(new Uri("/api/auth/login", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task Save_ValidatesInput_AndRequiresASecretTheFirstTime()
    {
        using var admin = await factory.LoginAsync($"oidc-valid-{Guid.NewGuid():N}", UserRole.Admin);

        var badTenant = await admin.PutAsJsonAsync("/api/admin/settings/oidc", Request() with { TenantId = "https://evil.example" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, badTenant.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(badTenant));

        // 還沒有任何 secret（資料庫與部署設定都沒有）時必須提供
        await ResetWithoutLockoutAsync(admin);
        var noSecret = await admin.PutAsJsonAsync("/api/admin/settings/oidc", Request(secret: null), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, noSecret.StatusCode);
        Assert.Equal("CLIENT_SECRET_REQUIRED", await ProblemCodeAsync(noSecret));
    }

    [Fact]
    public async Task Test_ChecksTheTenantMetadata()
    {
        using var admin = await factory.LoginAsync($"oidc-test-{Guid.NewGuid():N}", UserRole.Admin);

        var ok = await admin.PostAsJsonAsync("/api/admin/settings/oidc/test", new TestOidcSettingsRequest(factory.Oidc.Settings.TenantId), Ct);
        var wrong = await admin.PostAsJsonAsync("/api/admin/settings/oidc/test", new TestOidcSettingsRequest(Guid.NewGuid().ToString()), Ct);
        var invalid = await admin.PostAsJsonAsync("/api/admin/settings/oidc/test", new TestOidcSettingsRequest("../../etc"), Ct);

        Assert.True((await ok.Content.ReadFromJsonAsync<OidcTestResponse>(JsonDefaults.Options, Ct))!.Ok);
        Assert.False((await wrong.Content.ReadFromJsonAsync<OidcTestResponse>(JsonDefaults.Options, Ct))!.Ok);
        Assert.False((await invalid.Content.ReadFromJsonAsync<OidcTestResponse>(JsonDefaults.Options, Ct))!.Ok);
    }

    private static async Task EnsureLocalAdminAsync(HttpClient admin)
    {
        var created = await admin.PostAsJsonAsync(
            "/api/admin/users",
            new CreateLocalUserRequest($"local-admin-{Guid.NewGuid():N}"[..30], "本機管理員", null, UserRole.Admin, "local-admin-password-123"),
            Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    private async Task DisableLocalAdminsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        foreach (var user in await db.Users.Where(u => u.Issuer == LocalAccounts.Issuer && u.Role == UserRole.Admin && u.Status == UserStatus.Active).ToListAsync(Ct))
        {
            user.Disable(DateTimeOffset.UtcNow);
        }

        await db.SaveChangesAsync(Ct);
    }

    private static async Task ResetWithoutLockoutAsync(HttpClient admin)
    {
        await EnsureLocalAdminAsync(admin);
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync("/api/admin/settings/oidc", Ct)).StatusCode);
    }
}
