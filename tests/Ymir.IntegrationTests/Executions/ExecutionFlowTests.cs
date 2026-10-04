using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ymir.IntegrationTests.Api;
using Ymir.Platform.Infrastructure.Persistence;
using Ymir.VibeMaker.Contracts.Executions;
using Ymir.VibeMaker.Infrastructure.Dev;

namespace Ymir.IntegrationTests.Executions;

/// <summary>以 Scripted harness 驗證 execution pipeline（SA §11、§14）：送出 → 背景執行 → SSE → 保存訊息。</summary>
public class ExecutionFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<(HttpClient Client, Guid ConversationId)> ArrangeAsync(string account)
    {
        var client = await factory.LoginAsync(account);
        var workspace = await client.CreateWorkspaceAsync("ws");
        var conversation = await client.CreateConversationAsync(workspace.Id, "chat");
        return (client, conversation.Id);
    }

    [Fact]
    public async Task SendMessage_StreamsEventsAndPersistsHistory()
    {
        var (client, conversationId) = await ArrangeAsync("flow-happy");
        using var _ = client;

        var (response, accepted) = await client.SendMessageAsync(conversationId, "hello agent");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal($"/api/executions/{accepted!.ExecutionId}/events", accepted.EventStreamUrl);

        var events = await client.ReadEventsAsync(accepted.EventStreamUrl);

        Assert.Equal(ExecutionEventNames.Status, events[0].EventType);
        Assert.Contains(events, e => e.EventType == ExecutionEventNames.ExecutionStarted);
        Assert.Contains(events, e => e.EventType == ExecutionEventNames.AssistantDelta && e.Data.GetProperty("text").GetString() == "hello agent");
        var completed = events[^1];
        Assert.Equal(ExecutionEventNames.ExecutionCompleted, completed.EventType);
        Assert.Equal(Enumerable.Range(1, events.Count).Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)), events.Select(e => e.EventId));

        var messages = await client.GetMessagesAsync(conversationId);
        Assert.Equal(["USER", "ASSISTANT"], messages.Select(m => m.Role));
        Assert.Equal(accepted.MessageId, messages[0].Id);
        Assert.Equal(completed.Data.GetProperty("messageId").GetGuid(), messages[1].Id);
        Assert.Contains("hello agent", messages[1].Content, StringComparison.Ordinal);
        Assert.All(messages, m => Assert.Equal(accepted.ExecutionId, m.ExecutionId));
    }

    [Fact]
    public async Task SameClientRequestId_ReturnsSameExecution()
    {
        var (client, conversationId) = await ArrangeAsync("flow-idempotent");
        using var _ = client;
        var clientRequestId = Guid.NewGuid();

        var (_, first) = await client.SendMessageAsync(conversationId, "once", clientRequestId);
        var (secondResponse, second) = await client.SendMessageAsync(conversationId, "once", clientRequestId);

        Assert.Equal(HttpStatusCode.Accepted, secondResponse.StatusCode);
        Assert.Equal(first!.ExecutionId, second!.ExecutionId);
        await client.ReadEventsAsync(first.EventStreamUrl);
        Assert.Equal(2, (await client.GetMessagesAsync(conversationId)).Count);
    }

    [Fact]
    public async Task SecondMessageWhileRunning_Returns409()
    {
        var (client, conversationId) = await ArrangeAsync("flow-conflict");
        using var _ = client;

        var (_, first) = await client.SendMessageAsync(conversationId, "first");
        var (second, _) = await client.SendMessageAsync(conversationId, "second");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("EXECUTION_CONFLICT", problem.GetProperty("code").GetString());
        await client.ReadEventsAsync(first!.EventStreamUrl);
    }

    [Fact]
    public async Task Cancel_RunningExecution_EndsWithCancelled()
    {
        var (client, conversationId) = await ArrangeAsync("flow-cancel");
        using var _ = client;
        var (_, accepted) = await client.SendMessageAsync(conversationId, "please stop me");

        // 等 Agent 開始輸出再取消
        await WaitForEventAsync(accepted!.ExecutionId, ExecutionEventNames.AssistantDelta);
        var cancel = await client.PostAsync(new Uri($"/api/executions/{accepted.ExecutionId}/cancel", UriKind.Relative), null, TestContext.Current.CancellationToken);
        var events = await client.ReadEventsAsync(accepted.EventStreamUrl);

        Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        Assert.Equal(ExecutionEventNames.ExecutionCancelled, events[^1].EventType);
        Assert.Equal("CANCELLED", await GetStatusAsync(accepted.ExecutionId));

        // 取消後可以再送新的訊息
        var (next, _) = await client.SendMessageAsync(conversationId, "next");
        Assert.Equal(HttpStatusCode.Accepted, next.StatusCode);
    }

    [Fact]
    public async Task LastEventId_ResumesAfterGivenSequence()
    {
        var (client, conversationId) = await ArrangeAsync("flow-resume");
        using var _ = client;
        var (_, accepted) = await client.SendMessageAsync(conversationId, "resume");
        var all = await client.ReadEventsAsync(accepted!.EventStreamUrl);

        var resumed = await client.ReadEventsAsync(accepted.EventStreamUrl, lastEventId: 3);

        Assert.Equal(all.Skip(3).Select(e => e.EventId), resumed.Select(e => e.EventId));
    }

    [Fact]
    public async Task DisabledUser_CannotStartExecution()
    {
        var (client, conversationId) = await ArrangeAsync("flow-disabled");
        using var _ = client;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var user = await db.Users.SingleAsync(u => u.Subject == "flow-disabled", TestContext.Current.CancellationToken);
            user.Disable(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var (response, _) = await client.SendMessageAsync(conversationId, "blocked");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EmptyContent_Returns400()
    {
        var (client, conversationId) = await ArrangeAsync("flow-empty");
        using var _ = client;
        var (response, _) = await client.SendMessageAsync(conversationId, "  ");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task WaitForEventAsync(Guid executionId, string eventType)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<VibeMaker.Infrastructure.Persistence.VibeMakerDbContext>();
            if (await db.ExecutionEvents.AnyAsync(e => e.ExecutionId == executionId && e.EventType == eventType, TestContext.Current.CancellationToken))
            {
                return;
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Timed out waiting for {eventType}");
    }

    private async Task<string> GetStatusAsync(Guid executionId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VibeMaker.Infrastructure.Persistence.VibeMakerDbContext>();
        return await db.Database.SqlQuery<string>($"SELECT status AS Value FROM vibemaker.agent_executions WHERE id = {executionId}")
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    // ScriptedAgentHarness 每一步延遲，讓取消 / 衝突測試有時間介入。
    static ExecutionFlowTests() => ScriptedAgentHarness.StepDelay = TimeSpan.FromMilliseconds(200);
}
