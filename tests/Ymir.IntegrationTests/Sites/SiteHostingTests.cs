using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.Executions;
using Ymir.IntegrationTests.PlatformMcp;
using Ymir.SiteHost;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Contracts.Executions;
using Ymir.VibeMaker.Domain;
using Ymir.VibeMaker.Infrastructure.Runtime;

namespace Ymir.IntegrationTests.Sites;

/// <summary>API（發布）+ SiteHost（提供網站）：ADR-0016 的公開網站流程。兩者共用測試資料庫與網站 volume。</summary>
public sealed class SiteApiFactory : ApiFactory
{
    public const string BaseHost = "sites.test";

    private readonly List<WebApplication> _hosts = [];

    public string SitesRoot { get; } = Path.Combine(Path.GetTempPath(), "ymir-sites-" + Guid.NewGuid().ToString("N"));

    protected override void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting("Ymir:Sites:BaseUrl", $"http://{BaseHost}");
        builder.UseSetting("Ymir:Sites:Root", SitesRoot);
        builder.UseSetting("Ymir:Sites:MaxFiles", "20");
        builder.UseSetting("Ymir:Sites:MaxSitesPerUser", "3");
    }

    /// <summary>啟動 SiteHost（API 先啟動，資料庫才已經 migrate）。</summary>
    public async Task<HttpClient> StartSiteHostAsync()
    {
        _ = Services;
        var port = McpGatewayApiFactory.FreePort();
        var host = SiteHostApp.Build(
        [
            $"--urls=http://127.0.0.1:{port}",
            $"--SiteHost:BaseUrl=http://{BaseHost}",
            $"--SiteHost:Root={SitesRoot}",
            $"--SiteHost:PlatformUrl=http://ymir.test",
            $"--SiteHost:DataProtectionKeysPath={Path.Combine(SitesRoot, ".keys")}",
            $"--ConnectionStrings:ymir={DatabaseConnectionString}",
        ]);
        await host.StartAsync(TestContext.Current.CancellationToken);
        _hosts.Add(host);
        return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    }

    public override async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.StopAsync();
            await host.DisposeAsync();
        }

        await base.DisposeAsync();
        if (Directory.Exists(SitesRoot))
        {
            Directory.Delete(SitesRoot, recursive: true);
        }
    }
}

public class SiteHostingTests(SiteApiFactory factory) : IClassFixture<SiteApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly SemaphoreSlim s_start = new(1, 1);
    private static HttpClient? s_siteHost;

    internal async Task<HttpClient> SiteHostAsync()
    {
        await s_start.WaitAsync(Ct);
        try
        {
            return s_siteHost ??= await factory.StartSiteHostAsync();
        }
        finally
        {
            s_start.Release();
        }
    }

    /// <summary>建立對話並執行一次（建立工作目錄），回傳工作目錄的 host 路徑。</summary>
    internal async Task<(Guid ConversationId, string Directory)> WorkspaceAsync(HttpClient client)
    {
        var conversation = await client.CreateConversationAsync(null, "site");
        var (_, sent) = await client.SendMessageAsync(conversation.Id, "hello");
        Assert.Equal(ExecutionEventNames.ExecutionCompleted, (await client.ReadEventsAsync(sent!.EventStreamUrl))[^1].EventType);
        var userId = (await client.GetFromJsonAsync<JsonElement>("/api/me", Ct)).GetProperty("id").GetGuid();
        var directory = UserDirectories.For(factory.WorkspaceRoot, userId).HostPathOf(RuntimePaths.WorkingDirectoryFor(conversation.Id, null));
        Directory.CreateDirectory(directory);
        return (conversation.Id, directory);
    }

    internal static async Task WriteSiteAsync(string directory, string folder, string marker)
    {
        var root = Path.Combine(directory, folder);
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        await File.WriteAllTextAsync(Path.Combine(root, "index.html"), $"<h1>{marker}</h1>", Ct);
        await File.WriteAllTextAsync(Path.Combine(root, "app.js"), "console.log(1)", Ct);
        await File.WriteAllTextAsync(Path.Combine(root, "assets", "logo.svg"), "<svg/>", Ct);
        await File.WriteAllTextAsync(Path.Combine(root, ".env"), "TOKEN=secret", Ct);
    }

    internal static async Task<SiteResponse> PublishAsync(HttpClient client, Guid conversationId, string source = "dist", bool spa = true)
    {
        using var response = await client.PostAsJsonAsync($"/api/conversations/{conversationId}/sites", new PublishSiteRequest("我的網站", source, spa), JsonDefaults.Options, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SiteResponse>(JsonDefaults.Options, Ct))!;
    }

    internal static async Task<HttpResponseMessage> VisitAsync(HttpClient siteHost, SiteResponse site, string path, string? cookie = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Host = site.Url!.Host;
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        return await siteHost.SendAsync(request, Ct);
    }

    [Fact]
    public async Task PublishedSite_IsServedOnItsOwnHost_WithSpaRouting()
    {
        var siteHost = await SiteHostAsync();
        using var client = await factory.LoginAsync($"site-pub-{Guid.NewGuid():N}");
        var (conversationId, directory) = await WorkspaceAsync(client);
        await WriteSiteAsync(directory, "dist", "v1");

        var site = await PublishAsync(client, conversationId);

        Assert.Equal(SiteStatus.Published, site.Status);
        Assert.EndsWith($".{SiteApiFactory.BaseHost}", site.Url!.Host, StringComparison.Ordinal);
        Assert.Equal(3, site.FileCount); // .env 不會被發布
        using var index = await VisitAsync(siteHost, site, "/");
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Equal("<h1>v1</h1>", await index.Content.ReadAsStringAsync(Ct));
        Assert.Equal("nosniff", index.Headers.GetValues("X-Content-Type-Options").Single());
        using var script = await VisitAsync(siteHost, site, "/app.js");
        Assert.Contains("javascript", script.Content.Headers.ContentType!.MediaType, StringComparison.Ordinal);
        using var svg = await VisitAsync(siteHost, site, "/assets/logo.svg");
        Assert.Equal("image/svg+xml", svg.Content.Headers.ContentType!.MediaType);
        // SPA：沒有副檔名的路徑回 index.html；不存在的資源與隱藏檔仍是 404。
        using var route = await VisitAsync(siteHost, site, "/orders/42");
        Assert.Equal("<h1>v1</h1>", await route.Content.ReadAsStringAsync(Ct));
        using var missing = await VisitAsync(siteHost, site, "/missing.js");
        using var hidden = await VisitAsync(siteHost, site, "/.env");
        using var traversal = await VisitAsync(siteHost, site, "/%2e%2e/%2e%2e/etc/passwd");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        // Kestrel 把 %2e%2e 正規化成網站內的路徑；SPA 模式回 index.html，絕不會讀到版本目錄以外的檔案。
        Assert.Equal("<h1>v1</h1>", await traversal.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Republish_SwitchesVersions_AndFailuresKeepTheCurrentOne()
    {
        var siteHost = await SiteHostAsync();
        using var client = await factory.LoginAsync($"site-rep-{Guid.NewGuid():N}");
        var (conversationId, directory) = await WorkspaceAsync(client);
        await WriteSiteAsync(directory, "dist", "v1");
        var site = await PublishAsync(client, conversationId);

        for (var i = 2; i <= 4; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "dist", "index.html"), $"<h1>v{i}</h1>", Ct);
            using var republish = await client.PostAsJsonAsync($"/api/sites/{site.Id}/publish", new RepublishSiteRequest(null, null, null), JsonDefaults.Options, Ct);
            Assert.Equal(HttpStatusCode.OK, republish.StatusCode);
        }

        Directory.CreateDirectory(Path.Combine(directory, "empty"));
        using var broken = await client.PostAsJsonAsync($"/api/sites/{site.Id}/publish", new RepublishSiteRequest(null, "empty", null), JsonDefaults.Options, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, broken.StatusCode);
        using var current = await VisitAsync(siteHost, site, "/");
        Assert.Equal("<h1>v4</h1>", await current.Content.ReadAsStringAsync(Ct));
        // 只保留最近 3 個版本的檔案。
        Assert.Equal(3, Directory.GetDirectories(Path.Combine(factory.SitesRoot, site.Id.ToString("N"))).Length);
    }

    [Fact]
    public async Task UnpublishAndDelete_TakeTheSiteOffline()
    {
        var siteHost = await SiteHostAsync();
        using var client = await factory.LoginAsync($"site-off-{Guid.NewGuid():N}");
        var (conversationId, directory) = await WorkspaceAsync(client);
        await WriteSiteAsync(directory, "dist", "v1");
        var site = await PublishAsync(client, conversationId);

        using (var unpublish = await client.PostAsync(new Uri($"/api/sites/{site.Id}/unpublish", UriKind.Relative), null, Ct))
        {
            Assert.Equal(SiteStatus.Unpublished, (await unpublish.Content.ReadFromJsonAsync<SiteResponse>(JsonDefaults.Options, Ct))!.Status);
        }

        await Task.Delay(TimeSpan.FromSeconds(11), Ct); // SiteHost 的網站快取 10 秒
        using var offline = await VisitAsync(siteHost, site, "/");
        Assert.Equal(HttpStatusCode.NotFound, offline.StatusCode);

        using var delete = await client.DeleteAsync(new Uri($"/api/sites/{site.Id}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(factory.SitesRoot, site.Id.ToString("N"))));
        Assert.Empty((await client.GetFromJsonAsync<SitesResponse>("/api/sites", JsonDefaults.Options, Ct))!.Sites);
    }

    [Fact]
    public async Task OwnersOnly_AndInvalidSourcesAreRejected()
    {
        using var alice = await factory.LoginAsync($"site-alice-{Guid.NewGuid():N}");
        using var bob = await factory.LoginAsync($"site-bob-{Guid.NewGuid():N}");
        var (conversationId, directory) = await WorkspaceAsync(alice);
        await WriteSiteAsync(directory, "dist", "alice");
        var site = await PublishAsync(alice, conversationId);

        using var steal = await bob.PostAsJsonAsync($"/api/conversations/{conversationId}/sites", new PublishSiteRequest("x", "dist"), JsonDefaults.Options, Ct);
        using var republish = await bob.PostAsJsonAsync($"/api/sites/{site.Id}/publish", new RepublishSiteRequest(null, null, null), JsonDefaults.Options, Ct);
        using var delete = await bob.DeleteAsync(new Uri($"/api/sites/{site.Id}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NotFound, steal.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, republish.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Empty((await bob.GetFromJsonAsync<SitesResponse>("/api/sites", JsonDefaults.Options, Ct))!.Sites);

        using var traversal = await alice.PostAsJsonAsync($"/api/conversations/{conversationId}/sites", new PublishSiteRequest("x", "../other"), JsonDefaults.Options, Ct);
        using var noIndex = await alice.PostAsJsonAsync($"/api/conversations/{conversationId}/sites", new PublishSiteRequest("x", "dist/assets"), JsonDefaults.Options, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, traversal.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noIndex.StatusCode);
        Assert.Single((await alice.GetFromJsonAsync<SitesResponse>("/api/sites", JsonDefaults.Options, Ct))!.Sites); // 失敗的發布不留下網站

        for (var i = 0; i < 25; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "dist", $"page{i}.html"), "x", Ct);
        }

        using var tooMany = await alice.PostAsJsonAsync($"/api/sites/{site.Id}/publish", new RepublishSiteRequest(null, null, null), JsonDefaults.Options, Ct);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooMany.StatusCode);
    }
}
