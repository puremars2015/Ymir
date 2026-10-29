using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ymir.VibeMaker.Application.Connectors.OneDrive;
using Ymir.VibeMaker.Infrastructure.Connectors.OneDrive;

namespace Ymir.UnitTests.Connectors;

/// <summary>ADR-0013 / ADR-0030：OneDrive 名稱規則、Entra 端點推導、Graph 限流的重試間隔。</summary>
public class OneDriveRulesTests
{
    [Theory]
    [InlineData(3, 1)]
    [InlineData(55, 2)]
    public async Task GraphListing_StopsAtTheRequestedMetadataBudget(int limit, int pages)
    {
        using var handler = new GraphPageHandler();
        using var http = new HttpClient(handler);
        var graph = new GraphOneDriveClient(http, Options.Create(new OneDriveOptions { GraphBaseUrl = new Uri("https://graph.example/v1.0") }), NullLogger<GraphOneDriveClient>.Instance);
        var items = await graph.ListChildrenAsync("fixture-token", "folder", TestContext.Current.CancellationToken, limit);
        Assert.Equal(limit, items.Count);
        Assert.Equal(pages, handler.Pages);
    }

    private sealed class GraphPageHandler : HttpMessageHandler
    {
        public int Pages { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Pages++;
            Assert.Equal("Bearer fixture-token", request.Headers.Authorization?.ToString());
            var body = new Dictionary<string, object>
            {
                ["value"] = Enumerable.Range(0, 50).Select(i => new { id = $"{Pages}-{i}", name = $"{i}.txt" }).ToArray(),
                ["@odata.nextLink"] = "https://graph.example/v1.0/page",
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
        }
    }

    [Theory]
    [InlineData("own", false, true)]
    [InlineData("other", false, false)]
    [InlineData("own", true, false)]
    [InlineData(null, false, false)]
    public void Browser_OnlyAcceptsOwnDriveAndRejectsRemoteShortcuts(string? driveId, bool remote, bool expected)
    {
        var item = new DriveItemInfo("item", "file", false, null, 1, DateTimeOffset.UtcNow, driveId, "parent", remote);
        Assert.Equal(expected, OneDriveBrowser.BelongsToDrive(item, "own"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("item\n")]
    public void Browser_RejectsInvalidIds(string itemId) =>
        Assert.Throws<OneDriveItemInvalidException>(() => OneDriveBrowser.ValidateItemId(itemId));

    [Fact]
    public void GraphMetadata_HandlesMissingParentAndDetectsRemoteItems()
    {
        using var document = System.Text.Json.JsonDocument.Parse("""{"id":"item","parentReference":null,"remoteItem":{"id":"foreign"}}""");
        var item = GraphOneDriveClient.ToItem(document.RootElement);
        Assert.Null(item.DriveId);
        Assert.True(item.IsRemote);
    }

    [Theory]
    [InlineData("Ymir", true)]
    [InlineData("專案 1", true)]
    [InlineData("", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("name.", false)]
    [InlineData("a:b", false)]
    [InlineData("a*b", false)]
    [InlineData("a\u0001b", false)]
    public void Names_RejectCharactersOneDriveDoesNotAccept(string name, bool expected) =>
        Assert.Equal(expected, OneDrivePaths.IsValidName(name));

    [Theory]
    [InlineData("https://login.microsoftonline.com/tid/v2.0", "https://login.microsoftonline.com/tid/oauth2/v2.0/token")]
    [InlineData("http://127.0.0.1:5299/tid/v2.0", "http://127.0.0.1:5299/tid/oauth2/v2.0/token")]
    public void TokenEndpoint_IsDerivedFromAuthority(string authority, string expected)
    {
        Assert.Equal(expected, new OneDriveOAuthSettings(authority, "client", "secret").Endpoint("token").ToString());
    }

    [Fact]
    public void Settings_ToString_DoesNotLeakSecret()
    {
        Assert.DoesNotContain("super-secret", new OneDriveOAuthSettings("https://a/b/v2.0", "client", "super-secret").ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("token-value", new OneDriveTokens("token-value", "refresh-value", DateTimeOffset.UtcNow).ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(120, 30)]
    public void RetryAfter_IsHonoredUpToALimit(int seconds, int expected)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));

        Assert.Equal(TimeSpan.FromSeconds(expected), GraphOneDriveClient.RetryDelay(response));
    }

    [Fact]
    public void AuthorizeUri_UsesPkceAndOfflineAccess()
    {
        var client = new OneDriveOAuthClient(new HttpClient(), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<OneDriveOAuthClient>.Instance);

        var uri = client.BuildAuthorizeUri(
            new OneDriveOAuthSettings("https://login.microsoftonline.com/tid/v2.0", "client", "secret"),
            new Uri("https://ymir.example/api/connectors/onedrive/callback"),
            "state-1",
            "challenge-1",
            "alice@example.com").ToString();

        Assert.StartsWith("https://login.microsoftonline.com/tid/oauth2/v2.0/authorize?", uri, StringComparison.Ordinal);
        Assert.Contains("code_challenge=challenge-1", uri, StringComparison.Ordinal);
        Assert.Contains("code_challenge_method=S256", uri, StringComparison.Ordinal);
        Assert.Contains("offline_access", Uri.UnescapeDataString(uri), StringComparison.Ordinal);
        Assert.DoesNotContain("secret", uri, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("預算.xlsx", "預算.xlsx")]
    [InlineData("../../etc/passwd", "_.._etc_passwd")]
    [InlineData(".env", "env")]
    [InlineData("...", "未命名檔案")]
    [InlineData("node_modules", "未命名檔案")]
    [InlineData("a\nb", "a_b")]
    public void AgentDownload_LocalFileName_IsASafeSingleSegment(string name, string expected)
    {
        var local = OneDriveAgentTools.LocalFileName(name);
        Assert.Equal(expected, local);
        Assert.True(Ymir.VibeMaker.Application.Files.WorkspacePathRules.IsSafeRelativePath($"{OneDrivePaths.AgentDirectory}/{local}"));
    }
}
