using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.Executions;
using Ymir.IntegrationTests.PlatformMcp;
using Ymir.Platform.Users;
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

    /// <summary>清掉 SiteHost 的網站與授權快取，讓測試不必等 10 / 30 秒。</summary>
    public void ClearSiteHostCaches()
    {
        foreach (var host in _hosts)
        {
            ((MemoryCache)host.Services.GetRequiredService<IMemoryCache>()).Clear();
        }
    }

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

    internal static async Task SetAccessAsync(HttpClient owner, SiteResponse site, SiteAccessMode mode, params Guid[] userIds)
    {
        using var response = await owner.PutAsJsonAsync($"/api/sites/{site.Id}/access", new SiteAccessRequest(mode, userIds), JsonDefaults.Options, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<Guid> UserIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/me", Ct)).GetProperty("id").GetGuid();

    /// <summary>平台簽發票據 → SiteHost 兌換，回傳網站 cookie（<c>name=value</c>）；簽發失敗時回傳 API 的狀態碼。</summary>
    private static async Task<(HttpStatusCode Status, string? Cookie)> SignInToSiteAsync(HttpClient siteHost, SiteResponse site, HttpClient viewer, string path = "/")
    {
        using var issue = await viewer.PostAsJsonAsync($"/api/sites/{site.Id}/ticket", new SiteTicketRequest(path), JsonDefaults.Options, Ct);
        if (issue.StatusCode != HttpStatusCode.OK)
        {
            return (issue.StatusCode, null);
        }

        var redirect = (await issue.Content.ReadFromJsonAsync<SiteTicketResponse>(JsonDefaults.Options, Ct))!.RedirectUrl;
        Assert.Equal(site.Url!.Host, redirect.Host);
        using var redeem = await VisitAsync(siteHost, site, redirect.PathAndQuery);
        Assert.Equal(HttpStatusCode.Redirect, redeem.StatusCode);
        Assert.Equal(path, redeem.Headers.Location!.OriginalString);
        var setCookie = Assert.Single(redeem.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", setCookie, StringComparison.OrdinalIgnoreCase); // 只限這個 hostname
        return (HttpStatusCode.OK, setCookie.Split(';')[0]);
    }

    [Fact]
    public async Task PrivateSite_RequiresTicketLogin_AndOnlySharedUsersAndAdminsCanView()
    {
        var siteHost = await SiteHostAsync();
        using var owner = await factory.LoginAsync($"site-own-{Guid.NewGuid():N}");
        using var friend = await factory.LoginAsync($"site-friend-{Guid.NewGuid():N}");
        using var stranger = await factory.LoginAsync($"site-stranger-{Guid.NewGuid():N}");
        using var admin = await factory.LoginAsync($"site-admin-{Guid.NewGuid():N}", UserRole.Admin);
        var friendId = await UserIdAsync(friend);
        var ownerId = await UserIdAsync(owner);
        var (conversationId, directory) = await WorkspaceAsync(owner);
        await WriteSiteAsync(directory, "dist", "private");
        var site = await PublishAsync(owner, conversationId);

        // 分享選擇器只回未停用的帳號、不含自己
        var found = await owner.GetFromJsonAsync<List<UserSearchResult>>("/api/users/search?q=site-", JsonDefaults.Options, Ct);
        Assert.Contains(found!, u => u.Id == friendId);
        Assert.DoesNotContain(found!, u => u.Id == ownerId);

        await SetAccessAsync(owner, site, SiteAccessMode.SelectedUsers, friendId);
        var listed = (await owner.GetFromJsonAsync<SitesResponse>("/api/sites", JsonDefaults.Options, Ct))!.Sites.Single(s => s.Id == site.Id);
        Assert.Equal(SiteAccessMode.SelectedUsers, listed.AccessMode);
        Assert.Equal(friendId, Assert.Single(listed.SharedWith).UserId);
        Assert.Contains((await friend.GetFromJsonAsync<List<SharedSiteResponse>>("/api/sites/shared-with-me", JsonDefaults.Options, Ct))!, s => s.Id == site.Id);
        Assert.DoesNotContain((await stranger.GetFromJsonAsync<List<SharedSiteResponse>>("/api/sites/shared-with-me", JsonDefaults.Options, Ct))!, s => s.Id == site.Id);
        factory.ClearSiteHostCaches();

        // 沒有網站 cookie：導向平台的 site-access 頁
        using (var anonymous = await VisitAsync(siteHost, site, "/orders/1?x=1"))
        {
            Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
            Assert.Equal($"http://ymir.test/site-access?site={site.Id:D}&path=%2Forders%2F1%3Fx%3D1", anonymous.Headers.Location!.ToString());
        }

        var (_, friendCookie) = await SignInToSiteAsync(siteHost, site, friend, "/orders/1");
        using (var viewed = await VisitAsync(siteHost, site, "/", friendCookie))
        {
            Assert.Equal(HttpStatusCode.OK, viewed.StatusCode);
            Assert.True(viewed.Headers.CacheControl is { Private: true, NoStore: true });
        }

        var (_, ownerCookie) = await SignInToSiteAsync(siteHost, site, owner);
        var (_, adminCookie) = await SignInToSiteAsync(siteHost, site, admin);
        using (var ownerView = await VisitAsync(siteHost, site, "/", ownerCookie))
        using (var adminView = await VisitAsync(siteHost, site, "/", adminCookie))
        {
            Assert.Equal(HttpStatusCode.OK, ownerView.StatusCode);
            Assert.Equal(HttpStatusCode.OK, adminView.StatusCode);
        }

        // 沒有分享的使用者拿不到票據；偽造的 cookie 等同未登入
        Assert.Equal(HttpStatusCode.Forbidden, (await SignInToSiteAsync(siteHost, site, stranger)).Status);
        using (var forged = await VisitAsync(siteHost, site, "/", $"{SiteRequestHandler.CookieName}=forged"))
        {
            Assert.Equal(HttpStatusCode.Redirect, forged.StatusCode);
        }

        // 所有 Ymir 使用者
        await SetAccessAsync(owner, site, SiteAccessMode.AllUsers);
        Assert.Equal(HttpStatusCode.OK, (await SignInToSiteAsync(siteHost, site, stranger)).Status);

        // 撤銷分享：快取到期後已發的 cookie 也失效
        await SetAccessAsync(owner, site, SiteAccessMode.SelectedUsers);
        factory.ClearSiteHostCaches();
        using (var revoked = await VisitAsync(siteHost, site, "/", friendCookie))
        {
            Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        }

        // 別人改不了存取設定
        using var hijack = await stranger.PutAsJsonAsync($"/api/sites/{site.Id}/access", new SiteAccessRequest(SiteAccessMode.Public, null), JsonDefaults.Options, Ct);
        Assert.Equal(HttpStatusCode.NotFound, hijack.StatusCode);
    }

    [Fact]
    public async Task Tickets_AreSingleUse_BoundToTheSite_AndDisabledUsersLoseAccess()
    {
        var siteHost = await SiteHostAsync();
        using var owner = await factory.LoginAsync($"site-tk-{Guid.NewGuid():N}");
        using var viewer = await factory.LoginAsync($"site-tkv-{Guid.NewGuid():N}");
        using var admin = await factory.LoginAsync($"site-tka-{Guid.NewGuid():N}", UserRole.Admin);
        var viewerId = await UserIdAsync(viewer);
        var (conversationId, directory) = await WorkspaceAsync(owner);
        await WriteSiteAsync(directory, "dist", "a");
        await WriteSiteAsync(directory, "other", "b");
        var siteA = await PublishAsync(owner, conversationId);
        var siteB = await PublishAsync(owner, conversationId, "other");
        await SetAccessAsync(owner, siteA, SiteAccessMode.SelectedUsers, viewerId);
        await SetAccessAsync(owner, siteB, SiteAccessMode.SelectedUsers);
        factory.ClearSiteHostCaches();

        // 重放、拿到別的網站兌換、開放式導向都失敗
        using var issue = await viewer.PostAsJsonAsync($"/api/sites/{siteA.Id}/ticket", new SiteTicketRequest("//evil.example/"), JsonDefaults.Options, Ct);
        var redirect = (await issue.Content.ReadFromJsonAsync<SiteTicketResponse>(JsonDefaults.Options, Ct))!.RedirectUrl;
        using (var wrongSite = await VisitAsync(siteHost, siteB, redirect.PathAndQuery))
        {
            Assert.Equal(HttpStatusCode.BadRequest, wrongSite.StatusCode);
        }

        string cookie;
        using (var redeem = await VisitAsync(siteHost, siteA, redirect.PathAndQuery))
        {
            Assert.Equal(HttpStatusCode.Redirect, redeem.StatusCode);
            Assert.Equal("/", redeem.Headers.Location!.OriginalString);
            cookie = redeem.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        }

        using (var replay = await VisitAsync(siteHost, siteA, redirect.PathAndQuery))
        {
            Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        }

        // cookie 綁定網站：拿到 B 等同未登入
        using (var otherSite = await VisitAsync(siteHost, siteB, "/", cookie))
        {
            Assert.Equal(HttpStatusCode.Redirect, otherSite.StatusCode);
        }

        using (var allowed = await VisitAsync(siteHost, siteA, "/", cookie))
        {
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        // 停用帳號立即（快取到期後）失去存取
        using (var disable = await admin.PostAsync(new Uri($"/api/admin/users/{viewerId}/disable", UriKind.Relative), null, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        }

        factory.ClearSiteHostCaches();
        using var disabled = await VisitAsync(siteHost, siteA, "/", cookie);
        Assert.Equal(HttpStatusCode.Forbidden, disabled.StatusCode);

        // 分享名單不接受停用的帳號
        using var invalid = await owner.PutAsJsonAsync($"/api/sites/{siteA.Id}/access", new SiteAccessRequest(SiteAccessMode.SelectedUsers, [viewerId]), JsonDefaults.Options, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
}
