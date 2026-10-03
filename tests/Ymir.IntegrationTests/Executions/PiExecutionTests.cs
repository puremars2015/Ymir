using Microsoft.AspNetCore.Hosting;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.PiAgent;
using Ymir.Testing.FakeLlm;
using Ymir.VibeMaker.Contracts.Executions;
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
    }

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
    public async Task CreateFile_ThroughApi_WritesIntoWorkspaceAndKeepsContext()
    {
        Assert.SkipUnless(PiHarnessFixture.IsPiOnPath(), "pi is not on PATH (npm i -g @earendil-works/pi-coding-agent@1.0.0)");
        using var client = await factory.LoginAsync("pi-user");
        var workspace = await client.CreateWorkspaceAsync("pi");
        var conversation = await client.CreateConversationAsync(workspace.Id, "pi chat");

        var (_, first) = await client.SendMessageAsync(conversation.Id, $"{FakeLlmScript.CreateFileMarker} 建立檔案");
        var firstEvents = await client.ReadEventsAsync(first!.EventStreamUrl);
        var (_, second) = await client.SendMessageAsync(conversation.Id, "剛剛做了什麼？");
        var secondEvents = await client.ReadEventsAsync(second!.EventStreamUrl);

        Assert.Equal(ExecutionEventNames.ExecutionCompleted, firstEvents[^1].EventType);
        Assert.Contains(firstEvents, e => e.EventType == ExecutionEventNames.ToolStarted);
        var file = Path.Combine(WorkspaceDirectories.For(factory.WorkspaceRoot, workspace.Id).Workspace, FakeLlmScript.CreatedFileName);
        Assert.Equal(FakeLlmScript.CreatedFileContent, await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));

        // 同一個 Conversation 的 AgentSession 續接：模型收到第 2 則使用者訊息
        var messages = await client.GetMessagesAsync(conversation.Id);
        Assert.Equal(ExecutionEventNames.ExecutionCompleted, secondEvents[^1].EventType);
        Assert.Equal("收到第 2 則使用者訊息：剛剛做了什麼？", messages[^1].Content);
    }
}
