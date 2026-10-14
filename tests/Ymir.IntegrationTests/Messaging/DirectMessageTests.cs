using System.Net;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.Platform.Users;

namespace Ymir.IntegrationTests.Messaging;

/// <summary>使用者之間的訊息（ADR-0023）：名單、收發、已讀、隱私與 SSE 即時通知。</summary>
public class DirectMessageTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task SendAndRead_UpdatesUnreadCountsAndHistory()
    {
        var ct = TestContext.Current.CancellationToken;
        using var alice = await factory.LoginAsync("dm-alice");
        using var bob = await factory.LoginAsync("dm-bob");
        var aliceId = await UserIdAsync(alice);
        var bobId = await UserIdAsync(bob);

        var sent = await SendAsync(alice, bobId, "  午安，簡報好了嗎？  ");
        Assert.Equal("午安，簡報好了嗎？", sent.Body);
        Assert.Equal(aliceId, sent.SenderId);
        Assert.Null(sent.ReadAt);
        await SendAsync(alice, bobId, "第二則");

        var bobContacts = await ContactsAsync(bob);
        var fromAlice = Assert.Single(bobContacts, c => c.UserId == aliceId);
        Assert.Equal(2, fromAlice.UnreadCount);
        Assert.NotNull(fromAlice.LastMessageAt);
        // 未讀的聯絡人排最前面
        Assert.Equal(aliceId, bobContacts[0].UserId);
        Assert.DoesNotContain(bobContacts, c => c.UserId == bobId);

        var history = await HistoryAsync(bob, aliceId);
        Assert.Equal(["午安，簡報好了嗎？", "第二則"], history.Messages.Select(m => m.Body));
        Assert.False(history.HasMore);

        using var read = await bob.PostAsync($"/api/direct-messages/{aliceId}/read", null, ct);
        read.EnsureSuccessStatusCode();
        var readResult = (await read.Content.ReadFromJsonAsync<DirectMessagesReadResponse>(JsonDefaults.Options, ct))!;
        Assert.Equal(2, readResult.Count);
        Assert.Equal(0, (await ContactsAsync(bob)).Single(c => c.UserId == aliceId).UnreadCount);
        // 寄件者看得到已讀時間
        Assert.All((await HistoryAsync(alice, bobId)).Messages, m => Assert.NotNull(m.ReadAt));
        // 寄件者自己的未讀數不受影響
        Assert.Equal(0, (await ContactsAsync(alice)).Single(c => c.UserId == bobId).UnreadCount);
    }

    [Fact]
    public async Task OthersConversations_AreNotVisible()
    {
        var ct = TestContext.Current.CancellationToken;
        using var alice = await factory.LoginAsync("dm-private-alice");
        using var bob = await factory.LoginAsync("dm-private-bob");
        using var eve = await factory.LoginAsync("dm-private-eve");
        var aliceId = await UserIdAsync(alice);
        var bobId = await UserIdAsync(bob);
        var secret = await SendAsync(alice, bobId, "只給 Bob 看");

        // 路徑上的 id 只是「對方」：Eve 用 Alice 的 id 只會看到 Eve 與 Alice 之間的訊息（沒有）。
        Assert.Empty((await HistoryAsync(eve, aliceId)).Messages);
        Assert.Empty((await HistoryAsync(eve, bobId)).Messages);
        // 拿別人訊息的 id 當游標也讀不到任何東西
        Assert.Empty((await HistoryAsync(eve, aliceId, $"?before={secret.Id}")).Messages);
        // 標記已讀不會動到別人的訊息
        using var read = await eve.PostAsync($"/api/direct-messages/{aliceId}/read", null, ct);
        read.EnsureSuccessStatusCode();
        Assert.Equal(1, (await ContactsAsync(bob)).Single(c => c.UserId == aliceId).UnreadCount);
    }

    [Fact]
    public async Task InvalidRecipientsAndBodies_AreRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        using var alice = await factory.LoginAsync("dm-invalid-alice");
        using var carol = await factory.LoginAsync("dm-invalid-carol");
        var aliceId = await UserIdAsync(alice);
        var carolId = await UserIdAsync(carol);

        await AssertStatusAsync(HttpStatusCode.NotFound, alice, aliceId, "給自己");
        await AssertStatusAsync(HttpStatusCode.NotFound, alice, Guid.NewGuid(), "不存在的人");
        await AssertStatusAsync(HttpStatusCode.BadRequest, alice, carolId, "   ");
        await AssertStatusAsync(HttpStatusCode.BadRequest, alice, carolId, new string('字', 4001));
        using (var missing = await alice.GetAsync($"/api/direct-messages/{Guid.NewGuid()}", ct))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        // 停用的帳號：不出現在名單、不能再收到訊息（與「不存在」相同回應，不透露帳號狀態）
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IUserDirectory>().SetStatusAsync(carolId, UserStatus.Disabled, ct);
        }

        Assert.DoesNotContain(await ContactsAsync(alice), c => c.UserId == carolId);
        await AssertStatusAsync(HttpStatusCode.NotFound, alice, carolId, "還在嗎？");
    }

    [Fact]
    public async Task History_IsPaged()
    {
        using var alice = await factory.LoginAsync("dm-page-alice");
        using var bob = await factory.LoginAsync("dm-page-bob");
        var aliceId = await UserIdAsync(alice);
        var bobId = await UserIdAsync(bob);
        for (var i = 1; i <= 3; i++)
        {
            await SendAsync(i % 2 == 0 ? bob : alice, i % 2 == 0 ? aliceId : bobId, $"第 {i} 則");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        var latest = await HistoryAsync(alice, bobId, "?take=2");
        Assert.True(latest.HasMore);
        Assert.Equal(["第 2 則", "第 3 則"], latest.Messages.Select(m => m.Body));
        var older = await HistoryAsync(alice, bobId, $"?take=2&before={latest.Messages[0].Id}");
        Assert.False(older.HasMore);
        Assert.Equal(["第 1 則"], older.Messages.Select(m => m.Body));
    }

    [Fact]
    public async Task Events_DeliverMessagesAndReadReceiptsInRealTime()
    {
        var ct = TestContext.Current.CancellationToken;
        using var alice = await factory.LoginAsync("dm-sse-alice");
        using var bob = await factory.LoginAsync("dm-sse-bob");
        var aliceId = await UserIdAsync(alice);
        var bobId = await UserIdAsync(bob);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await using var aliceEvents = await OpenEventsAsync(alice, timeout.Token);
        await using var bobEvents = await OpenEventsAsync(bob, timeout.Token);

        var sent = await SendAsync(alice, bobId, "即時嗎？");
        var received = await bobEvents.NextAsync("message", timeout.Token);
        Assert.Equal(sent.Id, received.GetProperty("id").GetGuid());
        Assert.Equal("即時嗎？", received.GetProperty("body").GetString());
        // 寄件者自己的連線也會收到（其他分頁同步）
        Assert.Equal(sent.Id, (await aliceEvents.NextAsync("message", timeout.Token)).GetProperty("id").GetGuid());

        using var read = await bob.PostAsync($"/api/direct-messages/{aliceId}/read", null, timeout.Token);
        read.EnsureSuccessStatusCode();
        var receipt = await aliceEvents.NextAsync("read", timeout.Token);
        Assert.Equal(bobId, receipt.GetProperty("readerId").GetGuid());
        Assert.Equal(aliceId, receipt.GetProperty("peerId").GetGuid());
    }

    [Fact]
    public async Task Endpoints_RequireLogin()
    {
        var ct = TestContext.Current.CancellationToken;
        using var anonymous = factory.CreateBrowserClient();
        foreach (var url in new[] { "/api/direct-messages/contacts", "/api/direct-messages/events", $"/api/direct-messages/{Guid.NewGuid()}" })
        {
            using var response = await anonymous.GetAsync(url, ct);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var send = await anonymous.PostAsJsonAsync($"/api/direct-messages/{Guid.NewGuid()}", new SendDirectMessageRequest("hi"), ct);
        Assert.Equal(HttpStatusCode.Unauthorized, send.StatusCode);
    }

    private static async Task AssertStatusAsync(HttpStatusCode expected, HttpClient client, Guid peerId, string text)
    {
        using var response = await client.PostAsJsonAsync($"/api/direct-messages/{peerId}", new SendDirectMessageRequest(text), TestContext.Current.CancellationToken);
        Assert.Equal(expected, response.StatusCode);
    }

    private static async Task<Guid> UserIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/me", TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();

    private static async Task<DirectMessageResponse> SendAsync(HttpClient client, Guid peerId, string text)
    {
        using var response = await client.PostAsJsonAsync($"/api/direct-messages/{peerId}", new SendDirectMessageRequest(text), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DirectMessageResponse>(JsonDefaults.Options, TestContext.Current.CancellationToken))!;
    }

    private static async Task<List<DirectContactResponse>> ContactsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<DirectContactResponse>>("/api/direct-messages/contacts", JsonDefaults.Options, TestContext.Current.CancellationToken))!;

    private static async Task<DirectMessagePageResponse> HistoryAsync(HttpClient client, Guid peerId, string query = "") =>
        (await client.GetFromJsonAsync<DirectMessagePageResponse>($"/api/direct-messages/{peerId}{query}", JsonDefaults.Options, TestContext.Current.CancellationToken))!;

    private static async Task<EventReader> OpenEventsAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync("/api/direct-messages/events", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var reader = new EventReader(response, SseParser.Create(stream, (_, data) => JsonDocument.Parse(data.ToArray()).RootElement.Clone()).EnumerateAsync(cancellationToken).GetAsyncEnumerator(cancellationToken));
        // 收到 ready 才代表伺服器已訂閱，之後送出的訊息一定會收到。
        await reader.NextAsync("ready", cancellationToken);
        return reader;
    }

    private sealed class EventReader(HttpResponseMessage response, IAsyncEnumerator<SseItem<JsonElement>> items) : IAsyncDisposable
    {
        public async Task<JsonElement> NextAsync(string eventType, CancellationToken cancellationToken)
        {
            while (await items.MoveNextAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();
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
