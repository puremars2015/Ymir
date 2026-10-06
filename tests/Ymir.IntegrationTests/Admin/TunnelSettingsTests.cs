using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ymir.Api.Auth;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.Platform.Users;

namespace Ymir.IntegrationTests.Admin;

/// <summary>
/// 系統設定：Cloudflare Tunnel（ADR-0010）。開發環境的 runtime 不是經由 runtime host，所以 token 管理不可用；
/// 對外網域可以設定（實際的 Host 限制只在 Ymir:PublicEdge 開啟時生效，見 PublicEdgeTests）。
/// </summary>
public class TunnelSettingsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()!;

    [Fact]
    public async Task TokenManagement_IsUnavailableWithoutARuntimeHost()
    {
        using var admin = await factory.LoginAsync($"tunnel-dev-{Guid.NewGuid():N}", UserRole.Admin);

        var settings = await admin.GetFromJsonAsync<TunnelSettingsResponse>("/api/admin/settings/tunnel", JsonDefaults.Options, Ct);
        using var set = await admin.PutAsJsonAsync("/api/admin/settings/tunnel/token", new SetTunnelTokenRequest(new string('A', 150)), Ct);

        Assert.False(settings!.ManagementAvailable);
        Assert.False(settings.PublicEdgeEnabled);
        Assert.Equal(HttpStatusCode.Conflict, set.StatusCode);
        Assert.Equal("TUNNEL_MANAGEMENT_UNAVAILABLE", await ProblemCodeAsync(set));
    }

    [Fact]
    public async Task PublicHostname_CanBeSetValidatedAndReset()
    {
        using var admin = await factory.LoginAsync($"tunnel-host-{Guid.NewGuid():N}", UserRole.Admin);

        using var invalid = await admin.PutAsJsonAsync("/api/admin/settings/tunnel/hostname", new SetPublicHostnameRequest("https://evil.example/path"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var set = await admin.PutAsJsonAsync("/api/admin/settings/tunnel/hostname", new SetPublicHostnameRequest(" Ymir.Example.COM "), Ct);
        var saved = await set.Content.ReadFromJsonAsync<TunnelSettingsResponse>(JsonDefaults.Options, Ct);
        Assert.Equal("ymir.example.com", saved!.Hostname);
        Assert.Equal(OidcSettingsSource.Database, saved.HostnameSource);
        Assert.Equal("https://ymir.example.com/signin-oidc", saved.RedirectUri);

        using var reset = await admin.PutAsJsonAsync("/api/admin/settings/tunnel/hostname", new SetPublicHostnameRequest(""), Ct);
        var after = await reset.Content.ReadFromJsonAsync<TunnelSettingsResponse>(JsonDefaults.Options, Ct);
        Assert.Null(after!.Hostname);
        Assert.Equal(OidcSettingsSource.None, after.HostnameSource);

        var audit = await admin.GetFromJsonAsync<AuditLogPageResponse>("/api/admin/audit?action=admin.settings.tunnel", JsonDefaults.Options, Ct);
        Assert.Contains(audit!.Items, i => i.Action == "admin.settings.tunnel.hostname");
        Assert.Contains(audit.Items, i => i.Action == "admin.settings.tunnel.hostname_reset");
    }
}
