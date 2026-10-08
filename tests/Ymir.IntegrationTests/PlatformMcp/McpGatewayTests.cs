using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.Executions;
using Ymir.IntegrationTests.PiAgent;
using Ymir.McpGateway;
using Ymir.Platform.Users;
using Ymir.Testing.FakeLlm;
using Ymir.Testing.FakeMcp;
using Ymir.VibeMaker.Application.PlatformMcp;
using Ymir.VibeMaker.Contracts.Executions;
using Ymir.VibeMaker.Contracts.Extensions;
using Ymir.VibeMaker.Domain;
using Ymir.VibeMaker.Infrastructure.Runtime;

namespace Ymir.IntegrationTests.PlatformMcp;

/// <summary>
/// API（真實 Pi + Fake LLM）+ MCP Gateway + Fake MCP（echo）：ADR-0012 B 階段 2 的端到端驗證。
/// gateway 與 API 共用測試資料庫（gateway 只寫稽核）。
/// </summary>
public sealed class McpGatewayApiFactory : ApiFactory
{
    public const string SigningKey = "integration-test-mcp-signing-key-0123456789";
    public const string BackendSecret = "fake-mcp-backend-secret";
    public const string CredentialEnv = "YMIR_IT_FAKE_MCP_CREDENTIAL";
    public const string ServerName = "echo-svc";

    private readonly Lazy<FakeLlmServer> _fakeLlm = new(() => FakeLlmServer.StartAsync(OperatingSystem.IsWindows() ? "http://0.0.0.0:0" : "http://127.0.0.1:0").GetAwaiter().GetResult());
    private readonly Lazy<FakeMcpServer> _fakeMcp = new(() => FakeMcpServer.StartAsync(BackendSecret).GetAwaiter().GetResult());
    private readonly List<WebApplication> _gateways = [];
    private readonly string _catalogPath = Path.Combine(Path.GetTempPath(), $"ymir-mcp-{Guid.NewGuid():N}.json");

    public McpGatewayApiFactory()
    {
        GatewayPort = FreePort();
        Environment.SetEnvironmentVariable(CredentialEnv, BackendSecret);
    }

    public int GatewayPort { get; }

    public Uri GatewayUrl => new($"http://127.0.0.1:{GatewayPort}");

    public FakeMcpServer FakeMcp => _fakeMcp.Value;

    public string CatalogPath
    {
        get
        {
            if (!File.Exists(_catalogPath))
            {
                File.WriteAllText(_catalogPath, $$"""
                    {"servers":[{"name":"{{ServerName}}","description":"測試用 echo 服務","url":"{{FakeMcp.McpUrl}}","credentialEnv":"{{CredentialEnv}}"}]}
                    """);
            }

            return _catalogPath;
        }
    }

    protected override void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting("VibeMaker:Harness", "Pi");
        var modelUrl = _fakeLlm.Value.BaseUrl;
        if (OperatingSystem.IsWindows())
            modelUrl = new UriBuilder(modelUrl) { Host = "host.docker.internal" }.Uri;
        builder.UseSetting("VibeMaker:Pi:ModelBaseUrl", modelUrl.ToString());
        builder.UseSetting("VibeMaker:Pi:ModelId", FakeLlmEndpoints.ModelId);
        builder.UseSetting("VibeMaker:Pi:DevelopmentApiKey", "integration-test-key");
        builder.UseSetting("VibeMaker:Pi:AutoRetry", "false");
        builder.UseSetting("Ymir:Mcp:GatewayUrl", OperatingSystem.IsWindows()
            ? new UriBuilder(GatewayUrl) { Host = "host.docker.internal" }.Uri.ToString()
            : GatewayUrl.ToString());
        builder.UseSetting("Ymir:Mcp:CatalogPath", CatalogPath);
        builder.UseSetting("Ymir:Mcp:TokenSigningKey", SigningKey);
    }

    /// <summary>啟動 gateway（API 先啟動，資料庫才已經 migrate）；<paramref name="port"/> 為 null 時用 <see cref="GatewayPort"/>。</summary>
    public async Task<Uri> StartGatewayAsync(int requestsPerMinute = 1000, int? port = null)
    {
        _ = Services;
        var url = $"http://127.0.0.1:{port ?? GatewayPort}";
        var gateway = McpGatewayApp.Build(
        [
            $"--urls={(OperatingSystem.IsWindows() ? $"http://0.0.0.0:{port ?? GatewayPort}" : url)}",
            $"--McpGateway:CatalogPath={CatalogPath}",
            $"--McpGateway:TokenSigningKey={SigningKey}",
            $"--McpGateway:RequestsPerMinute={requestsPerMinute}",
            $"--ConnectionStrings:ymir={DatabaseConnectionString}",
        ]);
        await gateway.StartAsync(TestContext.Current.CancellationToken);
        _gateways.Add(gateway);
        return new Uri(url);
    }

    public static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public override async ValueTask DisposeAsync()
    {
        foreach (var gateway in _gateways)
        {
            await gateway.StopAsync();
            await gateway.DisposeAsync();
        }

        await base.DisposeAsync();
        if (_fakeLlm.IsValueCreated)
        {
            await _fakeLlm.Value.DisposeAsync();
        }

        if (_fakeMcp.IsValueCreated)
        {
            await _fakeMcp.Value.DisposeAsync();
        }

        File.Delete(_catalogPath);
    }
}

public class McpGatewayTests(McpGatewayApiFactory factory) : IClassFixture<McpGatewayApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly SemaphoreSlim s_gatewayStart = new(1, 1);
    private static Uri? s_gateway;

    private async Task<Uri> GatewayAsync()
    {
        await s_gatewayStart.WaitAsync(Ct);
        try
        {
            return s_gateway ??= await factory.StartGatewayAsync();
        }
        finally
        {
            s_gatewayStart.Release();
        }
    }

    private static string Token(Guid userId, params string[] servers) =>
        McpGatewayToken.Issue(McpGatewayApiFactory.SigningKey, new McpGatewayClaims(userId, servers, DateTimeOffset.UtcNow.AddMinutes(10)));

    private static Task<HttpResponseMessage> PostAsync(HttpClient http, Uri gateway, string? token, string server, object message)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(gateway, $"/mcp/{server}"))
        {
            Content = new StringContent(JsonSerializer.Serialize(message), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return http.SendAsync(request, Ct);
    }

    private static object ToolCall(string text) =>
        new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name = FakeMcpServer.EchoTool, arguments = new { text } } };

    private async Task<List<AuditLogItemResponse>> AuditAsync(string action)
    {
        using var admin = await factory.LoginAsync($"mcp-audit-{Guid.NewGuid():N}", UserRole.Admin);
        var page = await admin.GetFromJsonAsync<AuditLogPageResponse>($"/api/admin/audit?action={action}", JsonDefaults.Options, Ct);
        return page!.Items;
    }

    [Fact]
    public async Task Gateway_RejectsMissingForgedOrExpiredTokens()
    {
        var gateway = await GatewayAsync();
        using var http = new HttpClient();
        var userId = Guid.NewGuid();
        var expired = McpGatewayToken.Issue(McpGatewayApiFactory.SigningKey, new McpGatewayClaims(userId, [McpGatewayApiFactory.ServerName], DateTimeOffset.UtcNow.AddSeconds(-5)));
        var forged = McpGatewayToken.Issue("another-signing-key-that-is-long-enough-0123", new McpGatewayClaims(userId, [McpGatewayApiFactory.ServerName], DateTimeOffset.UtcNow.AddMinutes(5)));

        foreach (var token in new string?[] { null, "garbage", expired, forged })
        {
            using var response = await PostAsync(http, gateway, token, McpGatewayApiFactory.ServerName, ToolCall("x"));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        Assert.DoesNotContain(factory.FakeMcp.AuthorizationHeaders, h => h.Contains("ymcp1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Gateway_ForwardsWithTheBackendCredential_AndAuditsToolCalls()
    {
        var gateway = await GatewayAsync();
        using var http = new HttpClient();
        var userId = Guid.NewGuid();
        var token = Token(userId, McpGatewayApiFactory.ServerName);

        using var initialize = await PostAsync(http, gateway, token, McpGatewayApiFactory.ServerName,
            new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "test", version = "1" } } });
        using var call = await PostAsync(http, gateway, token, McpGatewayApiFactory.ServerName, ToolCall("hi"));

        Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);
        Assert.Equal(HttpStatusCode.OK, call.StatusCode);
        Assert.Contains("echo: hi", await call.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        // gateway 換成後端憑證，Agent 的 token 不會送到後端（ADR-0012 B.1、B.3）。
        Assert.All(factory.FakeMcp.AuthorizationHeaders, h => Assert.Equal($"Bearer {McpGatewayApiFactory.BackendSecret}", h));
        var audit = await AuditAsync("mcp.tool.call");
        var entry = Assert.Single(audit, a => a.ActorUserId == userId);
        Assert.Equal($"{McpGatewayApiFactory.ServerName}/{FakeMcpServer.EchoTool}", entry.TargetId);
        Assert.DoesNotContain("hi", entry.TargetId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gateway_DeniesServersNotGrantedByTheToken()
    {
        var gateway = await GatewayAsync();
        using var http = new HttpClient();
        var userId = Guid.NewGuid();

        using var notGranted = await PostAsync(http, gateway, Token(userId, "other"), McpGatewayApiFactory.ServerName, ToolCall("x"));
        using var unknown = await PostAsync(http, gateway, Token(userId, "unknown"), "unknown", ToolCall("x"));

        Assert.Equal(HttpStatusCode.Forbidden, notGranted.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unknown.StatusCode);
        Assert.Equal(2, (await AuditAsync("mcp.access.denied")).Count(a => a.ActorUserId == userId));
    }

    [Fact]
    public async Task Gateway_RateLimitsEachUser()
    {
        var limited = await factory.StartGatewayAsync(requestsPerMinute: 2, port: McpGatewayApiFactory.FreePort());
        using var http = new HttpClient();
        var alice = Token(Guid.NewGuid(), McpGatewayApiFactory.ServerName);
        var bob = Token(Guid.NewGuid(), McpGatewayApiFactory.ServerName);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var response = await PostAsync(http, limited, alice, McpGatewayApiFactory.ServerName, ToolCall("x"));
            statuses.Add(response.StatusCode);
        }

        using var other = await PostAsync(http, limited, bob, McpGatewayApiFactory.ServerName, ToolCall("x"));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    public async Task AccessList_DecidesWhoSeesPlatformServers()
    {
        using var admin = await factory.LoginAsync($"mcp-admin-{Guid.NewGuid():N}", UserRole.Admin);
        using var alice = await factory.LoginAsync($"mcp-alice-{Guid.NewGuid():N}");
        using var bob = await factory.LoginAsync($"mcp-bob-{Guid.NewGuid():N}");
        var aliceId = (await alice.GetFromJsonAsync<JsonElement>("/api/me", Ct)).GetProperty("id").GetGuid();

        var list = await admin.GetFromJsonAsync<PlatformMcpServersResponse>("/api/admin/mcp-servers", JsonDefaults.Options, Ct);
        Assert.True(list!.Enabled);
        Assert.Equal(McpGatewayApiFactory.ServerName, Assert.Single(list.Servers).Name);
        Assert.DoesNotContain("127.0.0.1", JsonSerializer.Serialize(list), StringComparison.Ordinal); // 不回後端位址

        using var save = await admin.PutAsJsonAsync($"/api/admin/mcp-servers/{McpGatewayApiFactory.ServerName}/access",
            new SaveMcpServerAccessRequest(true, McpAccessMode.SelectedUsers, [aliceId]), JsonDefaults.Options, Ct);
        using var unknown = await admin.PutAsJsonAsync("/api/admin/mcp-servers/unknown/access",
            new SaveMcpServerAccessRequest(true, McpAccessMode.Everyone, []), JsonDefaults.Options, Ct);

        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        var aliceExtensions = await alice.GetFromJsonAsync<MyExtensionsResponse>("/api/extensions", JsonDefaults.Options, Ct);
        var bobExtensions = await bob.GetFromJsonAsync<MyExtensionsResponse>("/api/extensions", JsonDefaults.Options, Ct);
        Assert.Equal(McpGatewayApiFactory.ServerName, Assert.Single(aliceExtensions!.PlatformMcpServers).Name);
        Assert.Empty(bobExtensions!.PlatformMcpServers);
        Assert.Contains(await AuditAsync("admin.mcp.access.update"), a => a.TargetId == McpGatewayApiFactory.ServerName);

        // 恢復成停用，避免影響其他測試。
        using var reset = await admin.PutAsJsonAsync($"/api/admin/mcp-servers/{McpGatewayApiFactory.ServerName}/access",
            new SaveMcpServerAccessRequest(false, McpAccessMode.Everyone, []), JsonDefaults.Options, Ct);
        reset.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Agent_CallsThePlatformToolThroughTheGateway_WithoutSeeingAnyCredential()
    {
        Assert.SkipUnless(PiHarnessFixture.IsPiOnPath(), "pi is not on PATH (npm i -g @earendil-works/pi-coding-agent@1.0.0)");
        await GatewayAsync();
        using var admin = await factory.LoginAsync($"mcp-pi-admin-{Guid.NewGuid():N}", UserRole.Admin);
        using var client = await factory.LoginAsync($"mcp-pi-{Guid.NewGuid():N}");
        var userId = (await client.GetFromJsonAsync<JsonElement>("/api/me", Ct)).GetProperty("id").GetGuid();
        using (var save = await admin.PutAsJsonAsync($"/api/admin/mcp-servers/{McpGatewayApiFactory.ServerName}/access",
            new SaveMcpServerAccessRequest(true, McpAccessMode.SelectedUsers, [userId]), JsonDefaults.Options, Ct))
        {
            save.EnsureSuccessStatusCode();
        }

        var conversation = await client.CreateConversationAsync(null, "mcp");
        var (_, sent) = await client.SendMessageAsync(conversation.Id, $"{FakeLlmScript.McpEchoMarker} 呼叫平台服務");
        var events = await client.ReadEventsAsync(sent!.EventStreamUrl);

        Assert.Equal(ExecutionEventNames.ExecutionCompleted, events[^1].EventType);
        var reply = (await client.GetMessagesAsync(conversation.Id))[^1].Content;
        Assert.Contains($"echo: {FakeLlmScript.McpEchoText}", reply, StringComparison.Ordinal);
        Assert.Contains(await AuditAsync("mcp.tool.call"), a => a.ActorUserId == userId);

        // mcp.json 只引用環境變數名稱，檔案裡沒有 token 值（ADR-0012 B.3）。
        var agentState = UserDirectories.For(factory.WorkspaceRoot, userId).AgentState;
        var mcpJson = await File.ReadAllTextAsync(Directory.GetFiles(agentState, "mcp.json", SearchOption.AllDirectories).Single(), Ct);
        Assert.Contains("${YMIR_MCP_TOKEN}", mcpJson, StringComparison.Ordinal);
        Assert.DoesNotContain("ymcp1.", mcpJson, StringComparison.Ordinal);
        Assert.DoesNotContain(McpGatewayApiFactory.BackendSecret, mcpJson, StringComparison.Ordinal);
    }
}
