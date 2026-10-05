using Microsoft.AspNetCore.Hosting;
using Ymir.IntegrationTests.Api;
using Ymir.VibeMaker.Contracts.Executions;

namespace Ymir.IntegrationTests.Executions;

public sealed class ShortTimeoutApiFactory : ApiFactory
{
    protected override void Configure(IWebHostBuilder builder) =>
        builder.UseSetting("VibeMaker:Runtime:ExecutionTimeoutMinutes", "0.005"); // 0.3 秒
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
