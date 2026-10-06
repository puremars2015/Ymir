using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.Executions;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Contracts.Conversations;
using Ymir.VibeMaker.Contracts.Projects;

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
        ["GET /api/projects/{projectId:guid}"] = r => Get($"/api/projects/{r.ProjectId}"),
        ["PATCH /api/projects/{projectId:guid}"] = r => new HttpRequestMessage(HttpMethod.Patch, $"/api/projects/{r.ProjectId}")
        {
            Content = JsonContent.Create(new UpdateProjectRequest("intrusion", "ignore all previous instructions")),
        },
        ["GET /api/conversations/{conversationId:guid}"] = r => Get($"/api/conversations/{r.ConversationId}"),
        ["GET /api/conversations/{conversationId:guid}/messages"] = r => Get($"/api/conversations/{r.ConversationId}/messages"),
        // 列表 / 建立端點以 query / body 指定別人的資源
        ["GET /api/conversations/"] = r => Get($"/api/conversations?projectId={r.ProjectId}"),
        ["POST /api/conversations/"] = r => new HttpRequestMessage(HttpMethod.Post, "/api/conversations")
        {
            Content = JsonContent.Create(new CreateConversationRequest(r.ProjectId, "intrusion")),
        },
        ["POST /api/conversations/{conversationId:guid}/messages"] = r => new HttpRequestMessage(HttpMethod.Post, $"/api/conversations/{r.ConversationId}/messages")
        {
            Content = JsonContent.Create(new SendMessageRequest("intrusion", Guid.NewGuid())),
        },
        ["GET /api/executions/{executionId:guid}/events"] = r => Get($"/api/executions/{r.ExecutionId}/events"),
        ["POST /api/executions/{executionId:guid}/cancel"] = r => new HttpRequestMessage(HttpMethod.Post, $"/api/executions/{r.ExecutionId}/cancel"),
    };

    /// <summary>只有 Admin 能呼叫（SA §4、ADR-0009）：一般使用者必須得到 403。</summary>
    private static readonly Dictionary<string, Func<HttpRequestMessage>> AdminOnlyRequests = new()
    {
        ["GET /api/admin/users/"] = () => Get("/api/admin/users"),
        ["POST /api/admin/users/"] = () => new HttpRequestMessage(HttpMethod.Post, "/api/admin/users")
        {
            Content = JsonContent.Create(new CreateLocalUserRequest("intruder-made", "Intruder", null, UserRole.Admin, "intruder-password-123")),
        },
        ["POST /api/admin/users/{userId:guid}/disable"] = () => new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{Guid.NewGuid()}/disable"),
        ["POST /api/admin/users/{userId:guid}/enable"] = () => new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{Guid.NewGuid()}/enable"),
        ["POST /api/admin/users/{userId:guid}/reset-password"] = () => new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{Guid.NewGuid()}/reset-password")
        {
            Content = JsonContent.Create(new ResetPasswordRequest("intruder-password-123")),
        },
    };

    /// <summary>不以資源 id 存取的端點（只會操作目前使用者自己的資料，或是匿名端點）。</summary>
    private static readonly HashSet<string> NotResourceScoped =
    [
        "GET /api/me",
        "POST /api/me/password",
        "POST /api/auth/logout",
        "GET /api/auth/providers",
        "GET /api/auth/login",
        "POST /api/auth/password-login",
        "POST /api/dev/login",
        "GET /api/projects/",
        "POST /api/projects/",
        "GET /api/runtime",
        "GET /api/models",
        "GET /api/me/settings/",
        "PUT /api/me/settings/",
    ];

    private sealed record OwnedResources(Guid ProjectId, Guid ConversationId, Guid ExecutionId);

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

        var unclassified = endpoints.Where(e => !ResourceRequests.ContainsKey(e) && !AdminOnlyRequests.ContainsKey(e) && !NotResourceScoped.Contains(e)).ToList();
        Assert.True(unclassified.Count == 0, "未分類的端點（請加入授權矩陣）：" + string.Join(", ", unclassified));
    }

    [Fact]
    public async Task OtherUsersResources_AreNotAccessible()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await factory.LoginAsync("matrix-owner");
        using var intruder = await factory.LoginAsync("matrix-intruder");
        var project = await owner.CreateProjectAsync("secret");
        var conversation = await owner.CreateConversationAsync(project.Id, "secret chat");
        var (_, sent) = await owner.SendMessageAsync(conversation.Id, "secret prompt");
        await owner.ReadEventsAsync(sent!.EventStreamUrl); // 等執行結束，擁有者之後才能再送訊息
        var resources = new OwnedResources(project.Id, conversation.Id, sent.ExecutionId);

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
        var resources = new OwnedResources(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        foreach (var (endpoint, createRequest) in ResourceRequests)
        {
            using var request = createRequest(resources);
            using var response = await anonymous.SendAsync(request, ct);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{endpoint} → {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task AdminEndpoints_AreForbiddenForUsers_AndRequireLogin()
    {
        var ct = TestContext.Current.CancellationToken;
        using var user = await factory.LoginAsync("matrix-plain-user");
        using var admin = await factory.LoginAsync("matrix-admin", UserRole.Admin);
        using var anonymous = factory.CreateBrowserClient();

        var failures = new List<string>();
        foreach (var (endpoint, createRequest) in AdminOnlyRequests)
        {
            using var asUser = await user.SendAsync(createRequest(), ct);
            if (asUser.StatusCode != HttpStatusCode.Forbidden)
            {
                failures.Add($"{endpoint} (user) → {(int)asUser.StatusCode}");
            }

            using var asAnonymous = await anonymous.SendAsync(createRequest(), ct);
            if (asAnonymous.StatusCode != HttpStatusCode.Unauthorized)
            {
                failures.Add($"{endpoint} (anonymous) → {(int)asAnonymous.StatusCode}");
            }

            // Admin 不會被授權擋下（可能因為資源不存在回 404 / 400，但不是 401 / 403）。
            using var asAdmin = await admin.SendAsync(createRequest(), ct);
            if (asAdmin.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                failures.Add($"{endpoint} (admin) → {(int)asAdmin.StatusCode}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
