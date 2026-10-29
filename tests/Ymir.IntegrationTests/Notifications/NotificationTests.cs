using System.Net;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.Executions;
using Ymir.Platform.Notifications;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Application.Agents;
using Ymir.VibeMaker.Contracts.Executions;

namespace Ymir.IntegrationTests.Notifications;

/// <summary>門檻設為 0 秒：每次完成或失敗都通知；harness 依訊息內容完成、失敗或等到被取消。</summary>
public sealed class NotificationApiFactory : ApiFactory
{
    protected override void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting("VibeMaker:Notifications:LongExecutionSeconds", "0");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAgentHarness>();
            services.AddSingleton<IAgentHarness, OutcomeHarness>();
        });
    }

    private sealed class OutcomeHarness : IAgentHarness
    {
        public async IAsyncEnumerable<AgentEvent> RunAsync(AgentRunRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (request.Prompt.Contains("[wait]", StringComparison.Ordinal))
            {
                yield return new AgentTextDelta("working");
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // 取消時不拋例外，以 AgentCancelled 結束（harness 的事件保證）。
                }

                yield return new AgentCancelled("working");
                yield break;
            }

            yield return request.Prompt.Contains("[fail]", StringComparison.Ordinal)
                ? new AgentFailed("AGENT_RUNTIME_ERROR", "Agent 執行環境發生錯誤。")
                : new AgentCompleted("done");
        }
    }
}

/// <summary>ADR-0032 N1：站內通知、使用者事件串流（/api/events）與長任務完成 / 失敗通知。</summary>
public class NotificationTests(NotificationApiFactory factory) : IClassFixture<NotificationApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> UserIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/me", Ct)).GetProperty("id").GetGuid();

    private static async Task<NotificationPageResponse> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<NotificationPageResponse>("/api/notifications", JsonDefaults.Options, Ct))!;

    /// <summary>送出並等到這次的通知寫入（通知在終止事件之後才建立，SSE 結束時可能還沒寫入）。</summary>
    private static async Task<(Guid ConversationId, Guid ExecutionId)> RunAsync(HttpClient client, string content)
    {
        var before = (await ListAsync(client)).Items.Count;
        var conversation = await client.CreateConversationAsync(null, "季報整理");
        var (_, sent) = await client.SendMessageAsync(conversation.Id, content);
        await client.ReadEventsAsync(sent!.EventStreamUrl);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while ((await ListAsync(client)).Items.Count == before)
        {
            Assert.True(DateTime.UtcNow < deadline, "the notification was not created");
            await Task.Delay(50, Ct);
        }

        return (conversation.Id, sent.ExecutionId);
    }

    [Fact]
    public async Task CompletedExecution_NotifiesTheOwner_ThroughTheEventStream()
    {
        using var client = await factory.LoginAsync($"notify-{Guid.NewGuid():N}");
        await using var events = await OpenEventsAsync(client);

        var (conversationId, _) = await RunAsync(client, "幫我整理");

        var pushed = (await events.NextAsync("notification")).Deserialize<NotificationResponse>(JsonDefaults.Options)!;
        Assert.Equal(NotificationKind.ExecutionCompleted, pushed.Kind);
        Assert.Equal("已完成：季報整理", pushed.Title);
        Assert.Equal($"/c/{conversationId:D}", pushed.Link);
        Assert.Null(pushed.ReadAt);

        var page = await ListAsync(client);
        Assert.Equal(1, page.UnreadCount);
        Assert.Equal(pushed.Id, Assert.Single(page.Items).Id);

        using var read = await client.PostAsync($"/api/notifications/{pushed.Id}/read", null, Ct);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(0, (await read.Content.ReadFromJsonAsync<NotificationUnreadResponse>(JsonDefaults.Options, Ct))!.UnreadCount);
        var readEvent = await events.NextAsync("notification-read");
        Assert.Equal(0, readEvent.GetProperty("unreadCount").GetInt32());
        Assert.NotNull(Assert.Single((await ListAsync(client)).Items).ReadAt);
    }

    [Fact]
    public async Task FailedExecution_NotifiesWithTheSummary_AndCancelledDoesNot()
    {
        using var client = await factory.LoginAsync($"notify-fail-{Guid.NewGuid():N}");

        await RunAsync(client, "[fail] 試試看");
        var failed = Assert.Single((await ListAsync(client)).Items);
        Assert.Equal(NotificationKind.ExecutionFailed, failed.Kind);
        Assert.Equal("執行失敗：季報整理", failed.Title);
        Assert.Equal("Agent 執行環境發生錯誤。", failed.Body);

        var conversation = await client.CreateConversationAsync(null, "取消的工作");
        var (_, sent) = await client.SendMessageAsync(conversation.Id, "[wait] 很久的工作");
        await WaitForRunningAsync(sent!.ExecutionId);
        using (var cancel = await client.PostAsync($"/api/executions/{sent.ExecutionId}/cancel", null, Ct))
        {
            Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        }

        var stream = await client.ReadEventsAsync(sent.EventStreamUrl);
        Assert.Equal(ExecutionEventNames.ExecutionCancelled, stream[^1].EventType);
        Assert.Single((await ListAsync(client)).Items); // 取消的不通知
    }

    [Fact]
    public async Task OtherUsersNotifications_AreNotVisible_AndReadAllOnlyTouchesMine()
    {
        using var alice = await factory.LoginAsync($"notify-alice-{Guid.NewGuid():N}");
        using var bob = await factory.LoginAsync($"notify-bob-{Guid.NewGuid():N}");
        await RunAsync(alice, "第一件");
        await RunAsync(alice, "第二件");
        await RunAsync(bob, "bob 的工作");
        var aliceItems = (await ListAsync(alice)).Items;
        Assert.Equal(2, aliceItems.Count);

        using (var foreign = await bob.PostAsync($"/api/notifications/{aliceItems[0].Id}/read", null, Ct))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }

        Assert.DoesNotContain((await ListAsync(bob)).Items, n => aliceItems.Any(a => a.Id == n.Id));
        using (var readAll = await bob.PostAsync("/api/notifications/read-all", null, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, readAll.StatusCode);
        }

        Assert.Equal(2, (await alice.GetFromJsonAsync<NotificationUnreadResponse>("/api/notifications/unread-count", JsonDefaults.Options, Ct))!.UnreadCount);
        Assert.Equal(0, (await bob.GetFromJsonAsync<NotificationUnreadResponse>("/api/notifications/unread-count", JsonDefaults.Options, Ct))!.UnreadCount);

        // 分頁：以最後一則的 id 當游標讀更舊的
        var first = await alice.GetFromJsonAsync<NotificationPageResponse>("/api/notifications?take=1", JsonDefaults.Options, Ct);
        Assert.True(first!.HasMore);
        var second = await alice.GetFromJsonAsync<NotificationPageResponse>($"/api/notifications?take=1&before={first.Items[0].Id}", JsonDefaults.Options, Ct);
        Assert.False(second!.HasMore);
        Assert.NotEqual(first.Items[0].Id, Assert.Single(second.Items).Id);
    }

    [Fact]
    public async Task Guests_GetNotificationsOnTheUserStream_ButNotTheMessagingStream()
    {
        using var guest = await factory.LoginAsync($"notify-guest-{Guid.NewGuid():N}", UserRole.Guest);
        await using var events = await OpenEventsAsync(guest);

        await RunAsync(guest, "訪客的工作");

        Assert.Equal("已完成：季報整理", (await events.NextAsync("notification")).GetProperty("title").GetString());
        using var legacy = await guest.GetAsync("/api/direct-messages/events", HttpCompletionOption.ResponseHeadersRead, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, legacy.StatusCode);
    }

    [Fact]
    public async Task DisablingTheAccount_DeletesUnreadNotifications_AndRetentionPurgesOldOnes()
    {
        using var client = await factory.LoginAsync($"notify-disabled-{Guid.NewGuid():N}");
        var userId = await UserIdAsync(client);
        await RunAsync(client, "工作");
        Assert.Equal(1, (await ListAsync(client)).UnreadCount);

        using (var admin = await factory.LoginAsync($"notify-admin-{Guid.NewGuid():N}", UserRole.Admin))
        using (var disable = await admin.PostAsync(new Uri($"/api/admin/users/{userId}/disable", UriKind.Relative), null, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        Assert.Equal(0, await notifications.CountUnreadAsync(userId, Ct));

        var other = await factory.LoginAsync($"notify-retention-{Guid.NewGuid():N}");
        var otherId = await UserIdAsync(other);
        other.Dispose();
        await notifications.CreateAsync(otherId, NotificationKind.ExecutionCompleted, "舊通知", null, null, null, Ct);
        Assert.True(await notifications.PurgeOlderThanAsync(DateTimeOffset.UtcNow.AddMinutes(1), Ct) >= 1);
        Assert.Equal(0, await notifications.CountUnreadAsync(otherId, Ct));
    }

    /// <summary>等 Agent 開始輸出再取消（與 ExecutionFlowTests 相同的做法）。</summary>
    private async Task WaitForRunningAsync(Guid executionId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<VibeMaker.Infrastructure.Persistence.VibeMakerDbContext>();
            if (await db.ExecutionEvents.AnyAsync(e => e.ExecutionId == executionId && e.EventType == ExecutionEventNames.AssistantDelta, Ct))
            {
                return;
            }

            await Task.Delay(50, Ct);
        }

        Assert.Fail("execution did not start");
    }

    private static async Task<EventReader> OpenEventsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead, Ct);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(Ct);
        var reader = new EventReader(response, SseParser.Create(stream, (_, data) => JsonDocument.Parse(data.ToArray()).RootElement.Clone()).EnumerateAsync(Ct).GetAsyncEnumerator(Ct));
        // 收到 ready 才代表伺服器已訂閱，之後的事件一定會收到。
        await reader.NextAsync("ready");
        return reader;
    }

    private sealed class EventReader(HttpResponseMessage response, IAsyncEnumerator<SseItem<JsonElement>> items) : IAsyncDisposable
    {
        public async Task<JsonElement> NextAsync(string eventType)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (await items.MoveNextAsync().AsTask().WaitAsync(timeout.Token))
            {
                if (items.Current.EventType == eventType)
                {
                    return items.Current.Data;
                }
            }

            throw new InvalidOperationException($"串流在收到 {eventType} 之前結束。");
        }

        public async ValueTask DisposeAsync()
        {
            await items.DisposeAsync();
            response.Dispose();
        }
    }
}

/// <summary>預設門檻（60 秒）：一般的短回覆不通知。</summary>
public class ShortExecutionNotificationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task ShortExecutions_AreNotNotified()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await factory.LoginAsync($"notify-short-{Guid.NewGuid():N}");
        var conversation = await client.CreateConversationAsync(null, "short");
        var (_, sent) = await client.SendMessageAsync(conversation.Id, "hello");
        await client.ReadEventsAsync(sent!.EventStreamUrl);

        var page = await client.GetFromJsonAsync<NotificationPageResponse>("/api/notifications", JsonDefaults.Options, ct);
        Assert.Empty(page!.Items);
        Assert.Equal(0, page.UnreadCount);
    }
}
