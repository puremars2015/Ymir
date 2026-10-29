using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.Executions;
using Ymir.Platform.Auditing;
using Ymir.Platform.Infrastructure.Persistence;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Application.PlatformMcp;
using Ymir.VibeMaker.Application.Plugins;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Contracts.Executions;
using Ymir.VibeMaker.Domain;
using Ymir.VibeMaker.Infrastructure.Runtime;

namespace Ymir.IntegrationTests.Connectors;

/// <summary>OneDrive 外掛（ADR-0030）：Fake OIDC / Fake Graph + 平台 MCP 設定（gateway 位址只用來啟用，測試直接呼叫 API 的內建端點）。</summary>
public sealed class OneDrivePluginApiFactory : OneDriveApiFactory
{
    public const string SigningKey = "integration-test-onedrive-plugin-key-0123456789";
    public const string CatalogServer = "docs";

    private readonly string _catalogPath = Path.Combine(Path.GetTempPath(), $"ymir-plugin-mcp-{Guid.NewGuid():N}.json");

    protected override void Configure(IWebHostBuilder builder)
    {
        base.Configure(builder);
        File.WriteAllText(_catalogPath, $$"""
            {"servers":[{"name":"{{CatalogServer}}","description":"公司文件","url":"http://127.0.0.1:9/mcp"}]}
            """);
        builder.UseSetting("Ymir:Mcp:GatewayUrl", "http://127.0.0.1:9/");
        builder.UseSetting("Ymir:Mcp:CatalogPath", _catalogPath);
        builder.UseSetting("Ymir:Mcp:TokenSigningKey", SigningKey);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        File.Delete(_catalogPath);
    }
}

public class OneDrivePluginTests(OneDrivePluginApiFactory factory) : IClassFixture<OneDrivePluginApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> UserIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/me", Ct)).GetProperty("id").GetGuid();

    private async Task AllowOneDriveAsync(Guid userId)
    {
        using var admin = await factory.LoginAsync($"plugin-admin-{Guid.NewGuid():N}", UserRole.Admin);
        using var response = await admin.PutAsJsonAsync(
            $"/api/admin/users/{userId}/extensions",
            new SaveUserExtensionsRequest(ExtensionGrantSetting.Inherit, ExtensionGrantSetting.Inherit, ExtensionGrantSetting.Inherit, ExtensionGrantSetting.Allow),
            JsonDefaults.Options,
            Ct);
        response.EnsureSuccessStatusCode();
    }

    private static Task<HttpResponseMessage> SetPluginAsync(HttpClient client, string key, bool enabled) =>
        client.PutAsJsonAsync($"/api/plugins/{Uri.EscapeDataString(key)}", new SetPluginEnabledRequest(enabled), Ct);

    private static async Task<PluginResponse> PluginAsync(HttpClient client, string key) =>
        (await client.GetFromJsonAsync<PluginResponse[]>("/api/plugins", JsonDefaults.Options, Ct))!.Single(p => p.Key == key);

    /// <summary>允許、連結並在市集啟用 OneDrive 的使用者。</summary>
    private async Task<(HttpClient Client, Guid UserId, string Account)> EnabledUserAsync()
    {
        var account = $"plugin-{Guid.NewGuid():N}";
        var client = await factory.LoginWithOidcAsync(account);
        var userId = await UserIdAsync(client);
        await AllowOneDriveAsync(userId);
        await OneDriveApiFactory.ConnectOneDriveAsync(client, account);
        using var enable = await SetPluginAsync(client, PluginService.OneDriveKey, true);
        enable.EnsureSuccessStatusCode();
        return (client, userId, account);
    }

    /// <summary>直接建立一個執行中的 execution（模擬 ExecutionRunner 已啟動 Agent）。</summary>
    private async Task<(Guid ConversationId, Guid ExecutionId)> RunningExecutionAsync(Guid userId, Guid? ownerId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IVibeMakerDbContext>();
        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation.Create(ownerId ?? userId, null, "plugin", now);
        var message = Message.CreateUser(conversation.Id, "hi", 1, now);
        var execution = AgentExecution.Queue(conversation, message, Guid.NewGuid(), now, invokedBy: ownerId is null ? null : userId);
        execution.Start(Guid.NewGuid(), Guid.NewGuid(), now);
        db.Conversations.Add(conversation);
        db.Messages.Add(message);
        db.AgentExecutions.Add(execution);
        await db.SaveChangesAsync(Ct);
        return (conversation.Id, execution.Id);
    }

    private static string Token(Guid userId, Guid? executionId, params string[] servers) =>
        McpGatewayToken.Issue(OneDrivePluginApiFactory.SigningKey, new McpGatewayClaims(userId, servers.Length == 0 ? ["onedrive"] : servers, DateTimeOffset.UtcNow.AddMinutes(10), executionId));

    private static HttpRequestMessage Rpc(string? token, string method, object? parameters = null, string? backendCredential = "")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, OneDriveMcpEndpoints.Path)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters ?? new { } }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", backendCredential == "" ? McpGatewayToken.BuiltInBackendCredential(OneDrivePluginApiFactory.SigningKey) : backendCredential ?? "x");
        if (token is not null)
        {
            request.Headers.Add(OneDriveMcpEndpoints.ForwardedTokenHeader, token);
        }

        return request;
    }

    /// <summary>呼叫工具：回傳 (isError, text)。</summary>
    private async Task<(bool IsError, string Text)> CallAsync(string token, string tool, object arguments)
    {
        using var client = factory.CreateClient();
        using var response = await client.SendAsync(Rpc(token, "tools/call", new { name = tool, arguments }), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var result = body.GetProperty("result");
        return (result.GetProperty("isError").GetBoolean(), result.GetProperty("content")[0].GetProperty("text").GetString()!);
    }

    private string HostDirectory(Guid userId, Guid conversationId)
    {
        var directory = UserDirectories.For(factory.WorkspaceRoot, userId).HostPathOf(RuntimePaths.WorkingDirectoryFor(conversationId, null));
        Directory.CreateDirectory(directory);
        return directory;
    }

    [Fact]
    public async Task Marketplace_OneDriveIsOffByDefault_AndNeedsTheCapabilityToEnable()
    {
        var account = $"plugin-market-{Guid.NewGuid():N}";
        using var client = await factory.LoginWithOidcAsync(account);
        var onedrive = await PluginAsync(client, PluginService.OneDriveKey);
        Assert.False(onedrive.Allowed);
        Assert.False(onedrive.Enabled);
        using (var refused = await SetPluginAsync(client, PluginService.OneDriveKey, true))
        {
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        await AllowOneDriveAsync(await UserIdAsync(client));
        onedrive = await PluginAsync(client, PluginService.OneDriveKey);
        Assert.True(onedrive.Allowed);
        Assert.False(onedrive.Installed);
        using (var enabled = await SetPluginAsync(client, PluginService.OneDriveKey, true))
        {
            enabled.EnsureSuccessStatusCode();
            var plugin = (await enabled.Content.ReadFromJsonAsync<PluginResponse>(JsonDefaults.Options, Ct))!;
            Assert.True(plugin.Enabled);
            Assert.False(plugin.AgentReady); // 還沒連結帳號
        }

        await OneDriveApiFactory.ConnectOneDriveAsync(client, account);
        Assert.True((await PluginAsync(client, PluginService.OneDriveKey)).AgentReady);
        using (var unknown = await SetPluginAsync(client, "mcp:not-in-catalog", true))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }
    }

    [Fact]
    public async Task Marketplace_UserCanTurnOffAPlatformService_AndRunsNoLongerIncludeIt()
    {
        var (client, userId, _) = await EnabledUserAsync();
        using (var admin = await factory.LoginAsync($"plugin-admin-{Guid.NewGuid():N}", UserRole.Admin))
        using (var access = await admin.PutAsJsonAsync(
            $"/api/admin/mcp-servers/{OneDrivePluginApiFactory.CatalogServer}/access",
            new SaveMcpServerAccessRequest(true, McpAccessMode.Everyone, null), JsonDefaults.Options, Ct))
        {
            access.EnsureSuccessStatusCode();
        }

        var key = PlatformMcpService.PluginKeyPrefix + OneDrivePluginApiFactory.CatalogServer;
        Assert.True((await PluginAsync(client, key)).Enabled); // 平台服務預設啟用
        var executionId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var grant = await scope.ServiceProvider.GetRequiredService<PlatformMcpService>().PrepareRunAsync(userId, executionId, TimeSpan.FromMinutes(5), includeOneDrive: true, Ct);
            Assert.Equal([OneDrivePluginApiFactory.CatalogServer, "onedrive"], grant!.Servers.Select(s => s.Name));
            Assert.Equal(executionId, McpGatewayToken.Validate(OneDrivePluginApiFactory.SigningKey, grant.Token, DateTimeOffset.UtcNow)!.ExecutionId);
            Assert.DoesNotContain(grant.Token, grant.ToString(), StringComparison.Ordinal);
        }

        using (var disable = await SetPluginAsync(client, key, false))
        {
            disable.EnsureSuccessStatusCode();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var grant = await scope.ServiceProvider.GetRequiredService<PlatformMcpService>().PrepareRunAsync(userId, executionId, TimeSpan.FromMinutes(5), includeOneDrive: false, Ct);
            Assert.Null(grant);
            var platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            Assert.True(await platform.AuditLog.AnyAsync(a => a.Action == "plugin.disable" && a.TargetId == key, Ct));
        }

        client.Dispose();
    }

    /// <summary>ADR-0033：OneDrive 只是 Agent 的外掛，執行前後不再同步工作目錄或對話紀錄。</summary>
    [Fact]
    public async Task Run_WithOneDriveEnabled_DoesNotSyncTheWorkspace()
    {
        var (client, _, account) = await EnabledUserAsync();
        var conversation = await client.CreateConversationAsync(null, "no sync");
        var (_, sent) = await client.SendMessageAsync(conversation.Id, "hello");
        var events = await client.ReadEventsAsync(sent!.EventStreamUrl);

        Assert.Equal(ExecutionEventNames.ExecutionCompleted, events[^1].EventType);
        Assert.DoesNotContain(events, e => e.Data.GetRawText().Contains("OneDrive", StringComparison.Ordinal));
        Assert.False(factory.Oidc.Graph.DriveOf(account).FolderExists("/Ymir"));
        client.Dispose();
    }

    [Fact]
    public async Task Tools_SearchDownloadUploadMoveDelete_InTheUsersOwnDrive()
    {
        var (client, userId, account) = await EnabledUserAsync();
        using var _ = client;
        var drive = factory.Oidc.Graph.DriveOf(account);
        drive.WriteFile("/Docs/budget.txt", "預算 2026"u8.ToArray());
        var (conversationId, executionId) = await RunningExecutionAsync(userId);
        var token = Token(userId, executionId);

        using (var http = factory.CreateClient())
        {
            using var init = await http.SendAsync(Rpc(token, "initialize", new { protocolVersion = "2025-06-18" }), Ct);
            Assert.Equal("ymir-onedrive", (await init.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());
            using var list = await http.SendAsync(Rpc(token, "tools/list"), Ct);
            var tools = (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("result").GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
            Assert.Equal(["search", "list_folder", "download", "upload", "create_folder", "move", "delete"], tools);
        }

        var (searchError, searchText) = await CallAsync(token, "search", new { query = "budget" });
        Assert.False(searchError);
        var budget = JsonDocument.Parse(searchText).RootElement.GetProperty("items")[0];
        var budgetId = budget.GetProperty("id").GetString()!;
        Assert.DoesNotContain("eTag", searchText, StringComparison.OrdinalIgnoreCase);

        var (rootError, rootText) = await CallAsync(token, "list_folder", new { });
        Assert.False(rootError);
        var docsId = JsonDocument.Parse(rootText).RootElement.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("name").GetString() == "Docs").GetProperty("id").GetString()!;

        var (downloadError, downloadText) = await CallAsync(token, "download", new { itemId = budgetId });
        Assert.False(downloadError, downloadText);
        Assert.Equal("onedrive/budget.txt", JsonDocument.Parse(downloadText).RootElement.GetProperty("path").GetString());
        var workspace = HostDirectory(userId, conversationId);
        Assert.Equal("預算 2026", await File.ReadAllTextAsync(Path.Combine(workspace, "onedrive", "budget.txt"), Ct));

        Directory.CreateDirectory(Path.Combine(workspace, "out"));
        await File.WriteAllTextAsync(Path.Combine(workspace, "out", "report.txt"), "第一版", Ct);
        Assert.False((await CallAsync(token, "upload", new { path = "out/report.txt", folderId = docsId })).IsError);
        Assert.Equal("第一版", Encoding.UTF8.GetString(drive.ReadFile("/Docs/report.txt")!));

        await File.WriteAllTextAsync(Path.Combine(workspace, "out", "report.txt"), "第二版", Ct);
        var (conflict, conflictText) = await CallAsync(token, "upload", new { path = "out/report.txt", folderId = docsId });
        Assert.True(conflict);
        Assert.Contains("overwrite", conflictText, StringComparison.Ordinal);
        Assert.Equal("第一版", Encoding.UTF8.GetString(drive.ReadFile("/Docs/report.txt")!)); // 沒有覆蓋
        Assert.False((await CallAsync(token, "upload", new { path = "out/report.txt", folderId = docsId, overwrite = true })).IsError);
        Assert.Equal("第二版", Encoding.UTF8.GetString(drive.ReadFile("/Docs/report.txt")!));
        Assert.True((await CallAsync(token, "upload", new { path = "../escape.txt" })).IsError);
        Assert.True((await CallAsync(token, "upload", new { path = "missing.txt" })).IsError);

        var (folderError, folderText) = await CallAsync(token, "create_folder", new { name = "Archive" });
        Assert.False(folderError);
        var archiveId = JsonDocument.Parse(folderText).RootElement.GetProperty("folder").GetProperty("id").GetString()!;
        var reportId = drive.FindByPathId("/Docs/report.txt");
        Assert.False((await CallAsync(token, "move", new { itemId = reportId, newParentId = archiveId, newName = "report-final.txt" })).IsError);
        Assert.NotNull(drive.ReadFile("/Archive/report-final.txt"));
        Assert.Null(drive.ReadFile("/Docs/report.txt"));

        Assert.False((await CallAsync(token, "delete", new { itemId = budgetId })).IsError);
        Assert.Contains("/Docs/budget.txt", drive.RecycleBin);
        Assert.Null(drive.ReadFile("/Docs/budget.txt"));
        Assert.True((await CallAsync(token, "delete", new { itemId = drive.RootId })).IsError);

        await using var scope = factory.Services.CreateAsyncScope();
        var platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var actions = await platform.AuditLog.Where(a => a.Actor == AuditActor.ForUser(userId) && a.Action.StartsWith("onedrive.agent.")).Select(a => a.Action).ToListAsync(Ct);
        Assert.Contains("onedrive.agent.upload", actions);
        Assert.Contains("onedrive.agent.create_folder", actions);
        Assert.Contains("onedrive.agent.move", actions);
        Assert.Contains("onedrive.agent.delete", actions);
        Assert.DoesNotContain(await platform.AuditLog.Select(a => a.TargetId).ToListAsync(Ct), id => id.Contains("budget", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Endpoint_RejectsCallsWithoutGatewayCredentialTokenOrRunningExecution()
    {
        var (client, userId, _) = await EnabledUserAsync();
        using var _ = client;
        var (_, executionId) = await RunningExecutionAsync(userId);
        using var http = factory.CreateClient();

        async Task<HttpStatusCode> StatusAsync(HttpRequestMessage request)
        {
            using (request)
            using (var response = await http.SendAsync(request, Ct))
            {
                return response.StatusCode;
            }
        }

        var token = Token(userId, executionId);
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(Rpc(token, "tools/list")));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(Rpc(token, "tools/list", backendCredential: null)));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(Rpc(token, "tools/list", backendCredential: token))); // Agent 的 token 不能當後端憑證
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(Rpc(null, "tools/list")));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(Rpc(Token(userId, null), "tools/list"))); // 沒有 execution
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(Rpc(Token(userId, executionId, "docs"), "tools/list"))); // 沒有 onedrive
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(Rpc(token[..^2] + "AA", "tools/list"))); // 簽章錯誤

        var viaTunnel = Rpc(token, "tools/list");
        viaTunnel.Headers.Add("Cf-Connecting-Ip", "203.0.113.5");
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(viaTunnel));

        // 別人的 execution、群組（共用 workspace）的 execution、已結束的 execution：都拒絕。
        var (otherClient, otherUserId, _) = await EnabledUserAsync();
        otherClient.Dispose();
        var (_, otherExecution) = await RunningExecutionAsync(otherUserId);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(Rpc(Token(userId, otherExecution), "tools/list")));
        var (_, groupExecution) = await RunningExecutionAsync(userId, ownerId: Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(Rpc(Token(userId, groupExecution), "tools/list")));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IVibeMakerDbContext>();
            var execution = await db.AgentExecutions.SingleAsync(e => e.Id == executionId, Ct);
            execution.Complete(null, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(Ct);
        }

        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(Rpc(token, "tools/list")));

        // 使用者在市集停用外掛後立即生效。
        var (_, nextExecution) = await RunningExecutionAsync(userId);
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(Rpc(Token(userId, nextExecution), "tools/list")));
        using (var disable = await SetPluginAsync(client, PluginService.OneDriveKey, false))
        {
            disable.EnsureSuccessStatusCode();
        }

        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(Rpc(Token(userId, nextExecution), "tools/list")));
    }

    [Fact]
    public async Task Tools_RejectItemsFromAnotherUsersDrive()
    {
        var (alice, aliceId, aliceAccount) = await EnabledUserAsync();
        var (bob, bobId, _) = await EnabledUserAsync();
        alice.Dispose();
        bob.Dispose();
        var aliceDrive = factory.Oidc.Graph.DriveOf(aliceAccount);
        aliceDrive.WriteFile("/secret.txt", "alice only"u8.ToArray());
        var secretId = aliceDrive.FindByPathId("/secret.txt");
        var (_, bobExecution) = await RunningExecutionAsync(bobId);
        var bobToken = Token(bobId, bobExecution);

        foreach (var (tool, arguments) in new (string, object)[]
        {
            ("download", new { itemId = secretId }),
            ("delete", new { itemId = secretId }),
            ("move", new { itemId = secretId, newName = "stolen.txt" }),
            ("list_folder", new { folderId = aliceDrive.RootId }),
        })
        {
            Assert.True((await CallAsync(bobToken, tool, arguments)).IsError, tool);
        }

        Assert.Equal("alice only", Encoding.UTF8.GetString(aliceDrive.ReadFile("/secret.txt")!));
        Assert.NotEqual(Guid.Empty, aliceId);
    }
}
