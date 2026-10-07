using Microsoft.AspNetCore.Hosting;
using Ymir.IntegrationTests.Api;
using Ymir.VibeMaker.Contracts.Executions;

namespace Ymir.IntegrationTests.Executions;

public sealed class ShortTimeoutApiFactory : ApiFactory
{
    protected override void Configure(IWebHostBuilder builder) =>
        builder.UseSetting("VibeMaker:Runtime:ExecutionTimeoutMinutes", "0.005"); // 0.3 秒
}

/// <summary>逾時極短：會在 Agent 啟動前的準備工作（例如建立交付目錄）期間發生。</summary>
public sealed class InstantTimeoutApiFactory : ApiFactory
{
    protected override void Configure(IWebHostBuilder builder) =>
        builder.UseSetting("VibeMaker:Runtime:ExecutionTimeoutMinutes", "0.0001"); // 約 6 毫秒
}

/// <summary>準備工作期間逾時也必須回報 AGENT_TIMEOUT，而不是執行環境錯誤（SA §13）。</summary>
public class ExecutionTimeoutDuringPreparationTests(InstantTimeoutApiFactory factory) : IClassFixture<InstantTimeoutApiFactory>
{
    [Fact]
    public async Task TimeoutBeforeTheAgentStarts_FailsWithAgentTimeout()
    {
        using var client = await factory.LoginAsync("instant-timeout-user");
        var conversation = await client.CreateConversationAsync(null, "chat");

        var (_, accepted) = await client.SendMessageAsync(conversation.Id, "slow task");
        var events = await client.ReadEventsAsync(accepted!.EventStreamUrl);

        Assert.Equal(ExecutionEventNames.ExecutionFailed, events[^1].EventType);
        Assert.Equal(ExecutionErrorCodes.AgentTimeout, events[^1].Data.GetProperty("code").GetString());
    }
}

/// <summary>逾時轉為 execution.failed（AGENT_TIMEOUT），並保存可讀的錯誤訊息（SA §13）。</summary>
public class ExecutionTimeoutTests(ShortTimeoutApiFactory factory) : IClassFixture<ShortTimeoutApiFactory>
{
    [Fact]
    public async Task ExecutionExceedingTimeout_FailsWithAgentTimeout()
    {
        using var client = await factory.LoginAsync("timeout-user");
        var project = await client.CreateProjectAsync("project");
        var conversation = await client.CreateConversationAsync(project.Id, "chat");

        var (_, accepted) = await client.SendMessageAsync(conversation.Id, "slow task");
        var events = await client.ReadEventsAsync(accepted!.EventStreamUrl);

        var failed = events[^1];
        Assert.Equal(ExecutionEventNames.ExecutionFailed, failed.EventType);
        Assert.Equal(ExecutionErrorCodes.AgentTimeout, failed.Data.GetProperty("code").GetString());

        var messages = await client.GetMessagesAsync(conversation.Id);
        Assert.Contains(messages, m => m.MessageType == "ERROR");
    }
}
