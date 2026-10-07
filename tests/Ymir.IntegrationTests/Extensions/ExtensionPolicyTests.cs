using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.Executions;
using Ymir.IntegrationTests.PiAgent;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Contracts.Executions;
using Ymir.VibeMaker.Contracts.Extensions;
using Ymir.VibeMaker.Infrastructure.Runtime;

namespace Ymir.IntegrationTests.Extensions;

/// <summary>
/// ADR-0012 A.2～A.6：管理員的擴充政策在啟動 Agent 時由伺服器強制。真實 Pi（Local runtime）+ Fake LLM，
/// 依模型收到的 system prompt 判斷 skill 是否被載入（與 A0 spike 的方法相同）。
/// </summary>
public class ExtensionPolicyTests(PiApiFactory factory) : IClassFixture<PiApiFactory>
{
    private const string UserSkillMarker = "USER_SKILL_MARKER_7f3a";
    private const string ExtensionMarker = "AGENT_EXTENSION_MARKER_91c2";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> UserIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/me", Ct)).GetProperty("id").GetGuid();

    private string AgentDirectoryOf(Guid userId) =>
        Path.Combine(UserDirectories.For(factory.WorkspaceRoot, userId).AgentState, "pi-agent");

    /// <summary>模擬 Agent 在先前的執行寫進 agent dir 的東西：skill、extension、舊的 mcp.json，以及讓專案受信任的設定。</summary>
    private async Task PlantAgentWrittenFilesAsync(Guid userId)
    {
        var agentDirectory = AgentDirectoryOf(userId);
        Directory.CreateDirectory(Path.Combine(agentDirectory, "skills", "user-hello"));
        await File.WriteAllTextAsync(
            Path.Combine(agentDirectory, "skills", "user-hello", "SKILL.md"),
            $"---\nname: user-hello\ndescription: {UserSkillMarker} says hello\n---\nSay hello.\n",
            Ct);
        Directory.CreateDirectory(Path.Combine(agentDirectory, "extensions"));
        await File.WriteAllTextAsync(
            Path.Combine(agentDirectory, "extensions", "marker.ts"),
            $"export default function (pi) {{ pi.on('before_agent_start', async (e) => ({{ systemPrompt: e.systemPrompt + ' {ExtensionMarker}' }})); }}\n",
            Ct);
        await File.WriteAllTextAsync(Path.Combine(agentDirectory, "mcp.json"), """{"mcpServers":{"legacy":{"command":"true"}}}""", Ct);
        await File.WriteAllTextAsync(Path.Combine(agentDirectory, "settings.json"), """{"defaultProjectTrust":"always"}""", Ct);
    }

    private async Task<string> RunAndCaptureSystemPromptAsync(HttpClient client)
    {
        var conversation = await client.CreateConversationAsync(null, "extensions");
        var (_, sent) = await client.SendMessageAsync(conversation.Id, "hi");
        var events = await client.ReadEventsAsync(sent!.EventStreamUrl);
        Assert.Equal(ExecutionEventNames.ExecutionCompleted, events[^1].EventType);
        var request = factory.FakeLlm.LastRequest!;
        return string.Join("\n", request["messages"]!.AsArray()
            .Where(m => m!["role"]!.GetValue<string>() is "system" or "developer")
            .Select(m => m!["content"]!.ToJsonString()));
    }

    [Fact]
    public async Task ByDefault_AgentWrittenSkillsExtensionsAndTrustAreIgnored_AndConfigIsRegenerated()
    {
        Assert.SkipUnless(PiHarnessFixture.IsPiOnPath(), "pi is not on PATH (npm i -g @earendil-works/pi-coding-agent@1.0.0)");
        using var client = await factory.LoginAsync("ext-default");
        var userId = await UserIdAsync(client);
        await client.CreateConversationAsync(null, "warm-up");
        await PlantAgentWrittenFilesAsync(userId);

        var systemPrompt = await RunAndCaptureSystemPromptAsync(client);

        Assert.DoesNotContain(UserSkillMarker, systemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(ExtensionMarker, systemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("ymir-extension-builder", systemPrompt, StringComparison.Ordinal);
        var agentDirectory = AgentDirectoryOf(userId);
        // 設定檔由 Ymir 每次執行前重寫；Agent 之前寫的 mcp.json 保留成 mcp.user.json，不會遺失。
        Assert.Equal("never", JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(agentDirectory, "settings.json"), Ct))!["defaultProjectTrust"]!.GetValue<string>());
        Assert.Empty(JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(agentDirectory, "mcp.json"), Ct))!["mcpServers"]!.AsObject());
        Assert.Contains("legacy", await File.ReadAllTextAsync(Path.Combine(agentDirectory, "mcp.user.json"), Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminGrant_EnablesSkillsForThatUser_AndExtensionsStayDisabled()
    {
        Assert.SkipUnless(PiHarnessFixture.IsPiOnPath(), "pi is not on PATH (npm i -g @earendil-works/pi-coding-agent@1.0.0)");
        using var admin = await factory.LoginAsync("ext-admin-grant", UserRole.Admin);
        using var client = await factory.LoginAsync("ext-granted");
        var userId = await UserIdAsync(client);
        await client.CreateConversationAsync(null, "warm-up");
        await PlantAgentWrittenFilesAsync(userId);

        using var grant = await admin.PutAsJsonAsync(
            $"/api/admin/users/{userId}/extensions", new SaveUserExtensionsRequest(ExtensionGrantSetting.Allow, ExtensionGrantSetting.Allow), JsonDefaults.Options, Ct);
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        var allowedPrompt = await RunAndCaptureSystemPromptAsync(client);

        Assert.Contains(UserSkillMarker, allowedPrompt, StringComparison.Ordinal);
        Assert.Contains("ymir-extension-builder", allowedPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(ExtensionMarker, allowedPrompt, StringComparison.Ordinal);
        // mcp 允許時，使用者的項目合併進這次的 mcp.json。
        var mcp = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AgentDirectoryOf(userId), "mcp.json"), Ct))!;
        Assert.NotNull(mcp["mcpServers"]!["legacy"]);

        // 改回禁止後，下一次執行立即生效（政策在每次執行時解析）。
        using var deny = await admin.PutAsJsonAsync(
            $"/api/admin/users/{userId}/extensions", new SaveUserExtensionsRequest(ExtensionGrantSetting.Deny, ExtensionGrantSetting.Inherit), JsonDefaults.Options, Ct);
        Assert.Equal(HttpStatusCode.OK, deny.StatusCode);
        var deniedPrompt = await RunAndCaptureSystemPromptAsync(client);
        Assert.DoesNotContain(UserSkillMarker, deniedPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminPolicyApi_SavesDefaults_OverridesPerUser_AndAudits()
    {
        using var admin = await factory.LoginAsync("ext-admin-api", UserRole.Admin);
        using var member = await factory.LoginAsync("ext-member-api");
        var memberId = await UserIdAsync(member);

        var initial = await admin.GetFromJsonAsync<ExtensionPolicyResponse>("/api/admin/settings/extensions", JsonDefaults.Options, Ct);
        // 預設：skill / MCP 關閉、對外連線允許（ADR-0012 A.8）；Local runtime 無法限制網路。
        Assert.Equal(new ExtensionValues(false, false, true, false), initial!.Defaults);
        Assert.Equal(Ymir.VibeMaker.Application.Runtime.RestrictedNetworkSupport.NotEnforced, initial.RestrictedNetwork);

        try
        {
            using var save = await admin.PutAsJsonAsync("/api/admin/settings/extensions", new SaveExtensionPolicyRequest(true, false, false), JsonDefaults.Options, Ct);
            var saved = await save.Content.ReadFromJsonAsync<ExtensionPolicyResponse>(JsonDefaults.Options, Ct);
            Assert.Equal(new ExtensionValues(true, false, false, false), saved!.Defaults);
            Assert.NotNull(saved.UpdatedAt);

            var inherited = await admin.GetFromJsonAsync<UserExtensionsResponse>($"/api/admin/users/{memberId}/extensions", JsonDefaults.Options, Ct);
            Assert.Equal(
                new UserExtensionsResponse(ExtensionGrantSetting.Inherit, ExtensionGrantSetting.Inherit, ExtensionGrantSetting.Inherit, ExtensionGrantSetting.Inherit, new ExtensionValues(true, false, false, false)),
                inherited);

            using var overrideResponse = await admin.PutAsJsonAsync(
                $"/api/admin/users/{memberId}/extensions", new SaveUserExtensionsRequest(ExtensionGrantSetting.Deny, ExtensionGrantSetting.Allow, ExtensionGrantSetting.Allow), JsonDefaults.Options, Ct);
            var overridden = await overrideResponse.Content.ReadFromJsonAsync<UserExtensionsResponse>(JsonDefaults.Options, Ct);
            Assert.Equal(new ExtensionValues(false, true, true, false), overridden!.Effective);

            // 成員只看得到自己的有效能力；還沒有執行環境時不建立 container，清單為空。
            var mine = (await member.GetFromJsonAsync<MyExtensionsResponse>("/api/extensions", JsonDefaults.Options, Ct))!;
            Assert.False(mine.SkillsAllowed);
            Assert.True(mine.McpAllowed);
            Assert.True(mine.InternetAllowed);
            Assert.False(mine.InventoryAvailable);
            Assert.Empty(mine.Skills);

            var audit = await admin.GetFromJsonAsync<AuditLogPageResponse>("/api/admin/audit?action=admin.user.extensions.update", JsonDefaults.Options, Ct);
            Assert.Contains(audit!.Items, i => i.TargetId == memberId.ToString("D"));
            var policyAudit = await admin.GetFromJsonAsync<AuditLogPageResponse>("/api/admin/audit?action=admin.settings.extensions.update", JsonDefaults.Options, Ct);
            Assert.NotEmpty(policyAudit!.Items);
        }
        finally
        {
            using var reset = await admin.PutAsJsonAsync("/api/admin/settings/extensions", new SaveExtensionPolicyRequest(false, false, true), JsonDefaults.Options, Ct);
            reset.EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task UnknownUser_Returns404()
    {
        using var admin = await factory.LoginAsync("ext-admin-404", UserRole.Admin);

        using var get = await admin.GetAsync($"/api/admin/users/{Guid.NewGuid()}/extensions", Ct);
        using var put = await admin.PutAsJsonAsync(
            $"/api/admin/users/{Guid.NewGuid()}/extensions", new SaveUserExtensionsRequest(ExtensionGrantSetting.Allow, ExtensionGrantSetting.Allow), JsonDefaults.Options, Ct);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
    }

    [Fact]
    public async Task MyExtensions_ListsNamesOnly_FromTheUsersRuntime()
    {
        Assert.SkipUnless(PiHarnessFixture.IsPiOnPath(), "pi is not on PATH (npm i -g @earendil-works/pi-coding-agent@1.0.0)");
        using var client = await factory.LoginAsync("ext-inventory");
        var userId = await UserIdAsync(client);
        await RunAndCaptureSystemPromptAsync(client); // 建立執行環境
        await PlantAgentWrittenFilesAsync(userId);
        await File.WriteAllTextAsync(
            Path.Combine(AgentDirectoryOf(userId), "mcp.user.json"), """{"mcpServers":{"erp":{"url":"https://erp.example","headers":{"Authorization":"secret-token"}}}}""", Ct);

        var body = await client.GetStringAsync("/api/extensions", Ct);
        var mine = JsonSerializer.Deserialize<MyExtensionsResponse>(body, JsonDefaults.Options)!;

        Assert.True(mine.InventoryAvailable);
        Assert.Equal(["user-hello"], mine.Skills);
        Assert.Equal(["erp"], mine.McpServers);
        // MCP 設定的內容（可能含憑證）不離開 runtime。
        Assert.DoesNotContain("secret-token", body, StringComparison.Ordinal);
    }
}
