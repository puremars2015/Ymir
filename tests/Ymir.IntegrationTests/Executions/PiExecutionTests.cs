using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.PiAgent;
using Ymir.Testing.FakeLlm;
using Ymir.VibeMaker.Application.Files;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Contracts.Executions;
using Ymir.VibeMaker.Contracts.Projects;
using Ymir.VibeMaker.Contracts.Settings;
using Ymir.VibeMaker.Infrastructure.Runtime;

namespace Ymir.IntegrationTests.Executions;

/// <summary>正式 API 流程 + 真實 Pi 程序（Local runtime）+ Fake LLM。</summary>
public sealed class PiApiFactory : ApiFactory
{
    private readonly Lazy<FakeLlmServer> _fakeLlm = new(() => FakeLlmServer.StartAsync().GetAwaiter().GetResult());

    protected override void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting("VibeMaker:Harness", "Pi");
        builder.UseSetting("VibeMaker:Pi:ModelBaseUrl", _fakeLlm.Value.BaseUrl.ToString());
        builder.UseSetting("VibeMaker:Pi:ModelId", FakeLlmEndpoints.ModelId);
        builder.UseSetting("VibeMaker:Pi:DevelopmentApiKey", "integration-test-key");
        builder.UseSetting("VibeMaker:Pi:AutoRetry", "false");
        builder.UseSetting("VibeMaker:Models:0:Id", FakeLlmEndpoints.ModelId);
        builder.UseSetting("VibeMaker:Models:1:Id", PiHarnessFixture.SecondModelId);
    }

    public FakeLlmState FakeLlm => _fakeLlm.Value.State;

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_fakeLlm.IsValueCreated)
        {
            await _fakeLlm.Value.DisposeAsync();
        }
    }
}

public class PiExecutionTests(PiApiFactory factory) : IClassFixture<PiApiFactory>
{
    [Fact]
    public async Task CreateFile_ThroughApi_WritesIntoProjectDirectoryAndKeepsContext()
    {
        Assert.SkipUnless(PiHarnessFixture.IsPiOnPath(), "pi is not on PATH (npm i -g @earendil-works/pi-coding-agent@1.0.0)");
        using var client = await factory.LoginAsync("pi-user");
        var project = await client.CreateProjectAsync("pi");
        var conversation = await client.CreateConversationAsync(project.Id, "pi chat");

        var (_, first) = await client.SendMessageAsync(conversation.Id, $"{FakeLlmScript.CreateFileMarker} 建立檔案");
        var firstEvents = await client.ReadEventsAsync(first!.EventStreamUrl);
        var (_, second) = await client.SendMessageAsync(conversation.Id, "剛剛做了什麼？");
        var secondEvents = await client.ReadEventsAsync(second!.EventStreamUrl);

        Assert.Equal(ExecutionEventNames.ExecutionCompleted, firstEvents[^1].EventType);
        Assert.Contains(firstEvents, e => e.EventType == ExecutionEventNames.ToolStarted);
        // 專案的檔案群組：{使用者目錄}/workspace/projects/{projectId}（ADR-0007）
        var directories = UserDirectories.For(factory.WorkspaceRoot, await GetUserIdAsync(client));
        var file = Path.Combine(directories.HostPathOf(RuntimePaths.ProjectDirectory(project.Id)), ArtifactService.DirectoryFor(first.ExecutionId), FakeLlmScript.CreatedFileName);
        Assert.Equal(FakeLlmScript.CreatedFileContent, await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));

        // 同一個 Conversation 的 AgentSession 續接：模型收到第 2 則使用者訊息
        var messages = await client.GetMessagesAsync(conversation.Id);
        Assert.Equal(ExecutionEventNames.ExecutionCompleted, secondEvents[^1].EventType);
        Assert.Equal("收到第 2 則使用者訊息：剛剛做了什麼？", messages[^1].Content);
    }

    [Fact]
    public async Task UngroupedConversation_WorksInItsOwnDirectory()
    {
        Assert.SkipUnless(PiHarnessFixture.IsPiOnPath(), "pi is not on PATH (npm i -g @earendil-works/pi-coding-agent@1.0.0)");
        using var client = await factory.LoginAsync("pi-loose");
        var conversation = await client.CreateConversationAsync(null, "loose chat");

        var (_, sent) = await client.SendMessageAsync(conversation.Id, $"{FakeLlmScript.CreateFileMarker} 建立檔案");
        var events = await client.ReadEventsAsync(sent!.EventStreamUrl);

        Assert.Equal(ExecutionEventNames.ExecutionCompleted, events[^1].EventType);
        var directories = UserDirectories.For(factory.WorkspaceRoot, await GetUserIdAsync(client));
        var file = Path.Combine(directories.HostPathOf(RuntimePaths.ConversationDirectory(conversation.Id)), ArtifactService.DirectoryFor(sent.ExecutionId), FakeLlmScript.CreatedFileName);
        Assert.True(File.Exists(file), "未分組對話的檔案應在 chats/{conversationId}");
        Assert.False(File.Exists(Path.Combine(directories.Workspace, FakeLlmScript.CreatedFileName)), "不應寫到使用者 workspace 根目錄");
    }

    [Fact]
    public async Task SelectedModel_AndPersonalPlusProjectSystemPrompts_ReachTheModel()
    {
        Assert.SkipUnless(PiHarnessFixture.IsPiOnPath(), "pi is not on PATH (npm i -g @earendil-works/pi-coding-agent@1.0.0)");
        var ct = TestContext.Current.CancellationToken;
        using var client = await factory.LoginAsync("pi-prompts");
        using var settings = await client.PutAsJsonAsync("/api/me/settings", new UpdateUserSettingsRequest("PERSONAL-PROMPT-MARKER 請用繁體中文"), ct);
        var project = await client.CreateProjectAsync("prompted");
        using var patch = await client.PatchAsJsonAsync($"/api/projects/{project.Id}", new UpdateProjectRequest(null, "PROJECT-PROMPT-MARKER 這是行銷網站"), ct);
        var conversation = await client.CreateConversationAsync(project.Id, "chat");

        var (_, sent) = await client.SendMessageAsync(conversation.Id, "hi", modelId: PiHarnessFixture.SecondModelId);
        var events = await client.ReadEventsAsync(sent!.EventStreamUrl);

        Assert.Equal(ExecutionEventNames.ExecutionCompleted, events[^1].EventType);
        var request = factory.FakeLlm.LastRequest!;
        Assert.Equal(PiHarnessFixture.SecondModelId, request["model"]!.GetValue<string>());
        var system = request["messages"]!.AsArray()
            .Where(m => m!["role"]!.GetValue<string>() is "system" or "developer")
            .Select(m => m!["content"]!.ToJsonString())
            .Single();
        var personal = system.IndexOf("PERSONAL-PROMPT-MARKER", StringComparison.Ordinal);
        var projectIndex = system.IndexOf("PROJECT-PROMPT-MARKER", StringComparison.Ordinal);
        Assert.True(personal >= 0 && projectIndex > personal, "個人 prompt 應在專案 prompt 之前，且兩者都送到模型");
        Assert.DoesNotContain("/agent-state/prompts", system, StringComparison.Ordinal); // 傳的是內容，不是路徑

        // 執行結束後 prompt 檔案已刪除
        var directories = UserDirectories.For(factory.WorkspaceRoot, await GetUserIdAsync(client));
        var promptDirectory = Path.Combine(directories.AgentState, "prompts");
        Assert.True(!Directory.Exists(promptDirectory) || Directory.GetFiles(promptDirectory).Length == 0);
    }

    private static async Task<Guid> GetUserIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<MeResponse>("/api/me", JsonDefaults.Options, TestContext.Current.CancellationToken))!.Id;
}
