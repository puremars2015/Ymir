using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ymir.IntegrationTests.Api;
using Ymir.VibeMaker.Contracts.Conversations;

namespace Ymir.IntegrationTests.Authorization;

/// <summary>
/// 驗收條件 #2：User A 無法透過修改 URL / payload 存取 User B 的資源（SA §12、§21）。
/// 每個 <c>/api</c> 端點都必須出現在 <see cref="ResourceRequests"/> 或 <see cref="NotResourceScoped"/>，
/// 新增端點沒有分類時 <see cref="EveryApiEndpointIsClassified"/> 會失敗。
/// </summary>
public class AuthorizationMatrixTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    /// <summary>以「另一個使用者的資源」呼叫，必須得到 404（或 403）。</summary>
    private static readonly Dictionary<string, Func<OwnedResources, HttpRequestMessage>> ResourceRequests = new()
    {
        ["GET /api/workspaces/{workspaceId:guid}"] = r => Get($"/api/workspaces/{r.WorkspaceId}"),
        ["GET /api/workspaces/{workspaceId:guid}/runtime"] = r => Get($"/api/workspaces/{r.WorkspaceId}/runtime"),
        ["GET /api/conversations/{conversationId:guid}"] = r => Get($"/api/conversations/{r.ConversationId}"),
        ["GET /api/conversations/{conversationId:guid}/messages"] = r => Get($"/api/conversations/{r.ConversationId}/messages"),
        // 列表 / 建立端點以 query / body 指定別人的資源
        ["GET /api/conversations/"] = r => Get($"/api/conversations?workspaceId={r.WorkspaceId}"),
        ["POST /api/conversations/"] = r => new HttpRequestMessage(HttpMethod.Post, "/api/conversations")
        {
            Content = JsonContent.Create(new CreateConversationRequest(r.WorkspaceId, "intrusion")),
        },
    };

    /// <summary>不以資源 id 存取的端點（只會操作目前使用者自己的資料，或是匿名端點）。</summary>
    private static readonly HashSet<string> NotResourceScoped =
    [
        "GET /api/me",
        "POST /api/auth/logout",
        "POST /api/dev/login",
        "GET /api/workspaces/",
        "POST /api/workspaces/",
        "GET /api/dev/agent-stream",
    ];

    private sealed record OwnedResources(Guid WorkspaceId, Guid ConversationId);

    private static HttpRequestMessage Get(string url) => new(HttpMethod.Get, url);

    [Fact]
    public void EveryApiEndpointIsClassified()
    {
        _ = factory.Server;
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api", StringComparison.Ordinal) == true)
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["ANY"]).Select(m => $"{m} {e.RoutePattern.RawText}"))
            .ToList();

        var unclassified = endpoints.Where(e => !ResourceRequests.ContainsKey(e) && !NotResourceScoped.Contains(e)).ToList();
        Assert.True(unclassified.Count == 0, "未分類的端點（請加入授權矩陣）：" + string.Join(", ", unclassified));
    }

    [Fact]
    public async Task OtherUsersResources_AreNotAccessible()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await factory.LoginAsync("matrix-owner");
        using var intruder = await factory.LoginAsync("matrix-intruder");
        var workspace = await owner.CreateWorkspaceAsync("secret");
        var conversation = await owner.CreateConversationAsync(workspace.Id, "secret chat");
        var resources = new OwnedResources(workspace.Id, conversation.Id);

        var failures = new List<string>();
        foreach (var (endpoint, createRequest) in ResourceRequests)
        {
            using var request = createRequest(resources);
            using var response = await intruder.SendAsync(request, ct);
            if (response.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.Forbidden))
            {
                failures.Add($"{endpoint} → {(int)response.StatusCode}");
            }

            // 擁有者本人必須能存取，避免測試因為請求本身錯誤而誤判通過。
            using var ownerRequest = createRequest(resources);
            using var ownerResponse = await owner.SendAsync(ownerRequest, ct);
            if (!ownerResponse.IsSuccessStatusCode)
            {
                failures.Add($"{endpoint} (owner) → {(int)ownerResponse.StatusCode}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task ResourceEndpoints_RequireLogin()
    {
        var ct = TestContext.Current.CancellationToken;
        using var anonymous = factory.CreateBrowserClient();
        var resources = new OwnedResources(Guid.NewGuid(), Guid.NewGuid());

        foreach (var (endpoint, createRequest) in ResourceRequests)
        {
            using var request = createRequest(resources);
            using var response = await anonymous.SendAsync(request, ct);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{endpoint} → {(int)response.StatusCode}");
        }
    }
}
