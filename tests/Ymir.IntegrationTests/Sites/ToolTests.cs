using System.Net;
using System.Net.Http.Json;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.Executions;
using Ymir.VibeMaker.Contracts.Executions;
using Ymir.VibeMaker.Domain;

namespace Ymir.IntegrationTests.Sites;

/// <summary>左側「小工具」（ADR-0029）：<c>/make</c> 小工具主題的對話執行成功後自動發布 <c>tool/index.html</c>，只有擁有者能看。</summary>
public class ToolTests(SiteApiFactory factory) : IClassFixture<SiteApiFactory>
{
    private static readonly Guid SmallToolTopicId = Guid.Parse("0199b7a0-0000-7000-8000-000000000001");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SiteHostingTests Sites => new(factory);

    [Fact]
    public async Task MakeToolConversation_PublishesPrivateToolAfterEachSuccessfulRun()
    {
        using var owner = await factory.LoginAsync($"tool-owner-{Guid.NewGuid():N}");
        using var other = await factory.LoginAsync($"tool-other-{Guid.NewGuid():N}");
        var (conversationId, directory) = await Sites.WorkspaceAsync(owner);

        // Agent 還在問需求、還沒有成果：不發布，也不留下網站。
        await SendAsync(owner, conversationId, "/make 小工具架設", SmallToolTopicId);
        Assert.Empty((await ToolsAsync(owner)).Tools);
        Assert.Empty((await SitesAsync(owner)).Sites);

        Directory.CreateDirectory(Path.Combine(directory, "tool"));
        await File.WriteAllTextAsync(Path.Combine(directory, "tool", "index.html"), "<h1>v1</h1>", Ct);
        await SendAsync(owner, conversationId, "做一個計算機");

        var tools = await ToolsAsync(owner);
        Assert.True(tools.Enabled);
        var tool = Assert.Single(tools.Tools);
        Assert.Equal(conversationId, tool.ConversationId);
        Assert.Equal("site", tool.Name);
        var site = Assert.Single((await SitesAsync(owner)).Sites);
        Assert.Equal(tool.Id, site.Id);
        Assert.Equal(SiteAccessMode.SelectedUsers, site.AccessMode);
        Assert.Equal("tool", site.SourcePath);
        Assert.Empty((await ToolsAsync(other)).Tools);

        // 之後的修改重新發布同一個網站
        await File.WriteAllTextAsync(Path.Combine(directory, "tool", "index.html"), "<h1>v2</h1>", Ct);
        await SendAsync(owner, conversationId, "按鈕改大一點");
        var republished = Assert.Single((await SitesAsync(owner)).Sites);
        Assert.Equal(site.Id, republished.Id);
        Assert.True(republished.PublishedAt > site.PublishedAt);

        // 別人拿不到票據
        using var ticket = await other.PostAsJsonAsync($"/api/sites/{site.Id}/ticket", new SiteTicketRequest("/"), JsonDefaults.Options, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, ticket.StatusCode);
    }

    [Fact]
    public async Task OrdinaryConversation_IsNotPublishedAsTool()
    {
        using var owner = await factory.LoginAsync($"tool-plain-{Guid.NewGuid():N}");
        var (conversationId, directory) = await Sites.WorkspaceAsync(owner);
        Directory.CreateDirectory(Path.Combine(directory, "tool"));
        await File.WriteAllTextAsync(Path.Combine(directory, "tool", "index.html"), "<h1>x</h1>", Ct);

        await SendAsync(owner, conversationId, "hello again");

        Assert.Empty((await ToolsAsync(owner)).Tools);
    }

    [Fact]
    public async Task SecurityHeaders_AllowFramingOnlyBetweenYmirAndSiteDomain()
    {
        using var client = await factory.LoginAsync($"tool-csp-{Guid.NewGuid():N}");
        using var response = await client.GetAsync(new Uri("/api/tools", UriKind.Relative), Ct);
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains($"frame-src http://*.{SiteApiFactory.BaseHost}", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);

        using var siteHost = await factory.StartSiteHostAsync();
        using var health = await siteHost.GetAsync(new Uri("/.ymir/health", UriKind.Relative), Ct);
        Assert.Equal("frame-ancestors 'self' http://ymir.test", health.Headers.GetValues("Content-Security-Policy").Single());
    }

    private static async Task SendAsync(HttpClient client, Guid conversationId, string text, Guid? topicId = null)
    {
        var (_, sent) = await client.SendMessageAsync(conversationId, text, makeTopicId: topicId);
        Assert.Equal(ExecutionEventNames.ExecutionCompleted, (await client.ReadEventsAsync(sent!.EventStreamUrl))[^1].EventType);
    }

    private static async Task<ToolsResponse> ToolsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ToolsResponse>("/api/tools", JsonDefaults.Options, Ct))!;

    private static async Task<SitesResponse> SitesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<SitesResponse>("/api/sites", JsonDefaults.Options, Ct))!;
}
