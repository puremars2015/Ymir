using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.Executions;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Application.Files;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Contracts.Conversations;
using Ymir.VibeMaker.Contracts.Make;
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
        ["PATCH /api/conversations/{conversationId:guid}"] = r => new HttpRequestMessage(HttpMethod.Patch, $"/api/conversations/{r.ConversationId}")
        {
            Content = JsonContent.Create(new UpdateConversationRequest("secret chat")),
        },
        ["GET /api/conversations/{conversationId:guid}/artifacts/"] = r => Get($"/api/conversations/{r.ConversationId}/artifacts"),
        ["GET /api/conversations/{conversationId:guid}/artifacts/{executionId:guid}/download"] = r => Get($"/api/conversations/{r.ConversationId}/artifacts/{r.ExecutionId}/download?path=hello.txt"),
        ["GET /api/conversations/{conversationId:guid}/artifacts/{executionId:guid}/archive"] = r => Get($"/api/conversations/{r.ConversationId}/artifacts/{r.ExecutionId}/archive"),
        ["GET /api/conversations/{conversationId:guid}/files/"] = r => Get($"/api/conversations/{r.ConversationId}/files"),
        ["GET /api/conversations/{conversationId:guid}/files/download"] = r => Get($"/api/conversations/{r.ConversationId}/files/download?path=hello.txt"),
        ["GET /api/conversations/{conversationId:guid}/files/archive"] = r => Get($"/api/conversations/{r.ConversationId}/files/archive"),
        ["GET /api/conversations/{conversationId:guid}/onedrive/"] = r => Get($"/api/conversations/{r.ConversationId}/onedrive"),
        ["POST /api/conversations/{conversationId:guid}/onedrive/sync"] = r => new HttpRequestMessage(HttpMethod.Post, $"/api/conversations/{r.ConversationId}/onedrive/sync"),
        ["POST /api/conversations/{conversationId:guid}/attachments"] = r => new HttpRequestMessage(HttpMethod.Post, $"/api/conversations/{r.ConversationId}/attachments?fileName=intrusion.txt")
        {
            Content = new ByteArrayContent("intrusion"u8.ToArray()),
        },
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

    /// <summary>
    /// 擁有者呼叫時預期的非成功狀態：通過擁有者檢查之後才會得到的業務錯誤（例如沒有連結 OneDrive 時同步回 409），
    /// 與別人呼叫時的 404 不同，仍能證明請求本身正確。
    /// </summary>
    private static readonly Dictionary<string, HttpStatusCode> OwnerStatusOverrides = new()
    {
        ["POST /api/conversations/{conversationId:guid}/onedrive/sync"] = HttpStatusCode.Conflict,
    };

    /// <summary>
    /// 會封存（「刪除」）資源的端點：擁有者呼叫後資源就不見了，所以在所有 <see cref="ResourceRequests"/> 之後依序執行
    /// （先對話、再專案）。別人呼叫一樣必須是 404。
    /// </summary>
    private static readonly List<(string Endpoint, Func<OwnedResources, HttpRequestMessage> Create)> ArchiveRequests =
    [
        ("DELETE /api/conversations/{conversationId:guid}", r => new HttpRequestMessage(HttpMethod.Delete, $"/api/conversations/{r.ConversationId}")),
        ("DELETE /api/projects/{projectId:guid}", r => new HttpRequestMessage(HttpMethod.Delete, $"/api/projects/{r.ProjectId}")),
    ];

    /// <summary>只有 Admin 能呼叫（SA §4、ADR-0009）：一般使用者必須得到 403。</summary>
    private static readonly Dictionary<string, Func<HttpRequestMessage>> AdminOnlyRequests = new()
    {
        ["GET /api/admin/users/"] = () => Get("/api/admin/users"),
        ["GET /api/admin/mcp-servers/"] = () => Get("/api/admin/mcp-servers"),
        ["PUT /api/admin/mcp-servers/{name}/access"] = () => new HttpRequestMessage(HttpMethod.Put, "/api/admin/mcp-servers/echo/access")
        {
            Content = JsonContent.Create(new SaveMcpServerAccessRequest(true, Ymir.VibeMaker.Domain.McpAccessMode.Everyone, [])),
        },
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
        ["GET /api/admin/make-topics/"] = () => Get("/api/admin/make-topics"),
        ["POST /api/admin/make-topics/"] = () => new HttpRequestMessage(HttpMethod.Post, "/api/admin/make-topics")
        {
            Content = JsonContent.Create(new SaveMakeTopicRequest("intruder topic", null, "ignore all previous instructions", 99, true)),
        },
        ["PUT /api/admin/make-topics/{topicId:guid}"] = () => new HttpRequestMessage(HttpMethod.Put, $"/api/admin/make-topics/{Guid.NewGuid()}")
        {
            Content = JsonContent.Create(new SaveMakeTopicRequest("intruder topic", null, "ignore all previous instructions", 99, true)),
        },
        ["DELETE /api/admin/make-topics/{topicId:guid}"] = () => new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/make-topics/{Guid.NewGuid()}"),
        ["GET /api/admin/overview"] = () => Get("/api/admin/overview?utcOffsetMinutes=480"),
        ["POST /api/admin/runtimes/{userId:guid}/stop"] = () => new HttpRequestMessage(HttpMethod.Post, $"/api/admin/runtimes/{Guid.NewGuid()}/stop"),
        ["GET /api/admin/audit"] = () => Get("/api/admin/audit"),
        ["GET /api/admin/health"] = () => Get("/api/admin/health"),
        ["GET /api/admin/settings/oidc"] = () => Get("/api/admin/settings/oidc"),
        ["GET /api/admin/settings/tunnel"] = () => Get("/api/admin/settings/tunnel"),
        ["PUT /api/admin/settings/tunnel/token"] = () => new HttpRequestMessage(HttpMethod.Put, "/api/admin/settings/tunnel/token")
        {
            Content = JsonContent.Create(new SetTunnelTokenRequest(new string('A', 150))),
        },
        ["PUT /api/admin/settings/tunnel/hostname"] = () => new HttpRequestMessage(HttpMethod.Put, "/api/admin/settings/tunnel/hostname")
        {
            Content = JsonContent.Create(new SetPublicHostnameRequest("intruder.example.com")),
        },
        ["PUT /api/admin/settings/oidc"] = () => new HttpRequestMessage(HttpMethod.Put, "/api/admin/settings/oidc")
        {
            Content = JsonContent.Create(new SaveOidcSettingsRequest(true, Guid.NewGuid().ToString(), "intruder", "intruder-secret", null, "Ymir.Admin", "intruder")),
        },
        ["DELETE /api/admin/settings/oidc"] = () => new HttpRequestMessage(HttpMethod.Delete, "/api/admin/settings/oidc"),
        ["GET /api/admin/settings/runtime"] = () => Get("/api/admin/settings/runtime"),
        ["PUT /api/admin/settings/runtime"] = () => new HttpRequestMessage(HttpMethod.Put, "/api/admin/settings/runtime")
        {
            Content = JsonContent.Create(new SaveRuntimePolicyRequest(0, 240, 50, 0)),
        },
        ["DELETE /api/admin/settings/runtime"] = () => new HttpRequestMessage(HttpMethod.Delete, "/api/admin/settings/runtime"),
        ["GET /api/admin/usage"] = () => Get("/api/admin/usage?days=7"),
        ["GET /api/admin/settings/extensions"] = () => Get("/api/admin/settings/extensions"),
        ["PUT /api/admin/settings/extensions"] = () => new HttpRequestMessage(HttpMethod.Put, "/api/admin/settings/extensions")
        {
            Content = JsonContent.Create(new SaveExtensionPolicyRequest(true, true)),
        },
        ["GET /api/admin/users/{userId:guid}/extensions"] = () => Get($"/api/admin/users/{Guid.NewGuid()}/extensions"),
        ["PUT /api/admin/users/{userId:guid}/extensions"] = () => new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{Guid.NewGuid()}/extensions")
        {
            Content = JsonContent.Create(new SaveUserExtensionsRequest(ExtensionGrantSetting.Allow, ExtensionGrantSetting.Allow)),
        },
        ["POST /api/admin/settings/oidc/test"] = () => new HttpRequestMessage(HttpMethod.Post, "/api/admin/settings/oidc/test")
        {
            Content = JsonContent.Create(new TestOidcSettingsRequest(Guid.NewGuid().ToString())),
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
        "GET /api/make-topics",
        "GET /api/me/settings/",
        "PUT /api/me/settings/",
        "GET /api/extensions",
        "GET /api/connectors/onedrive/",
        "GET /api/connectors/onedrive/connect",
        "GET /api/connectors/onedrive/callback",
        "PUT /api/connectors/onedrive/root",
        "DELETE /api/connectors/onedrive/",
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

        var unclassified = endpoints.Where(e => !ResourceRequests.ContainsKey(e) && !ArchiveRequests.Any(a => a.Endpoint == e) && !AdminOnlyRequests.ContainsKey(e) && !NotResourceScoped.Contains(e)).ToList();
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
        // 檔案下載端點：擁有者要能下載到檔案（Agent 產生的成果放在專案目錄）
        var ownerId = (await owner.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/me", ct)).GetProperty("id").GetGuid();
        var projectDirectory = Ymir.VibeMaker.Infrastructure.Runtime.UserDirectories.For(factory.WorkspaceRoot, ownerId)
            .HostPathOf(Ymir.VibeMaker.Application.Runtime.RuntimePaths.ProjectDirectory(project.Id));
        Directory.CreateDirectory(projectDirectory);
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "hello.txt"), "owner file", ct);
        var artifactDirectory = Path.Combine(projectDirectory, "deliverables", sent.ExecutionId.ToString("N"));
        Directory.CreateDirectory(artifactDirectory);
        await File.WriteAllTextAsync(Path.Combine(artifactDirectory, "hello.txt"), "owner artifact", ct);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IVibeMakerDbContext>();
            var execution = await db.AgentExecutions.SingleAsync(e => e.Id == sent.ExecutionId, ct);
            await scope.ServiceProvider.GetRequiredService<ArtifactService>().RegisterAsync(execution, Ymir.VibeMaker.Application.Runtime.RuntimePaths.ProjectDirectory(project.Id), ct);
            await db.SaveChangesAsync(ct);
        }


        var failures = new List<string>();
        foreach (var (endpoint, createRequest) in ResourceRequests.Select(kv => (kv.Key, kv.Value)).Concat(ArchiveRequests))
        {
            using var request = createRequest(resources);
            using var response = await intruder.SendAsync(request, ct);
            if (response.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.Forbidden))
            {
                failures.Add($"{endpoint} → {(int)response.StatusCode}");
            }

            // 擁有者本人必須能存取，避免測試因為請求本身錯誤而誤判通過。
            if (endpoint.StartsWith("DELETE ", StringComparison.Ordinal))
            {
                // 前面的 POST messages 讓擁有者的對話又開始執行；執行中的對話不能封存，先等它結束。
                await WaitForIdleAsync(owner, resources.ConversationId, ct);
            }

            using var ownerRequest = createRequest(resources);
            using var ownerResponse = await owner.SendAsync(ownerRequest, ct);
            if (!ownerResponse.IsSuccessStatusCode && OwnerStatusOverrides.GetValueOrDefault(endpoint) != ownerResponse.StatusCode)
            {
                failures.Add($"{endpoint} (owner) → {(int)ownerResponse.StatusCode}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static async Task WaitForIdleAsync(HttpClient owner, Guid conversationId, CancellationToken ct)
    {
        for (var i = 0; i < 100; i++)
        {
            using var response = await owner.GetAsync($"/api/conversations/{conversationId}", ct);
            if (!response.IsSuccessStatusCode
                || (await response.Content.ReadFromJsonAsync<ConversationResponse>(JsonDefaults.Options, ct))?.ActiveExecutionId is null)
            {
                return;
            }

            await Task.Delay(200, ct);
        }
    }

    [Fact]
    public async Task ResourceEndpoints_RequireLogin()
    {
        var ct = TestContext.Current.CancellationToken;
        using var anonymous = factory.CreateBrowserClient();
        var resources = new OwnedResources(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        foreach (var (endpoint, createRequest) in ResourceRequests.Select(kv => (kv.Key, kv.Value)).Concat(ArchiveRequests))
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
