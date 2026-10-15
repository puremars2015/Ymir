using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ymir.Api.Endpoints;
using Ymir.Api.Infrastructure;
using Ymir.IntegrationTests.Api;
using Ymir.Platform.Messaging;
using Ymir.VibeMaker.Contracts.Conversations;
using Ymir.VibeMaker.Infrastructure.Runtime;

namespace Ymir.IntegrationTests.Messaging;

/// <summary>群組聊天與 @bot（ADR-0026）：成員資格、擁有者權限、未讀、群組 Agent 的執行者與 runtime 擁有者。</summary>
public class ChatGroupTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> UserIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/me", Ct)).GetProperty("id").GetGuid();

    private static async Task<ChatGroupResponse> CreateAsync(HttpClient owner, string name, params Guid[] members)
    {
        using var response = await owner.PostAsJsonAsync("/api/chat-groups", new CreateChatGroupRequest(name, members), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ChatGroupResponse>(JsonDefaults.Options, Ct))!;
    }

    private static async Task<SendGroupMessageResponse> SendAsync(HttpClient client, Guid groupId, string text)
    {
        using var response = await client.PostAsJsonAsync($"/api/chat-groups/{groupId}/messages", new SendGroupMessageRequest(text, Guid.NewGuid()), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SendGroupMessageResponse>(JsonDefaults.Options, Ct))!;
    }

    private static async Task<List<ChatGroupSummaryResponse>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<ChatGroupSummaryResponse>>("/api/chat-groups", JsonDefaults.Options, Ct))!;

    [Fact]
    public async Task MembersChat_NonMembersSeeNothing_UnreadIsTracked()
    {
        using var alice = await factory.LoginAsync($"grp-alice-{Guid.NewGuid():N}");
        using var bob = await factory.LoginAsync($"grp-bob-{Guid.NewGuid():N}");
        using var eve = await factory.LoginAsync($"grp-eve-{Guid.NewGuid():N}");
        var bobId = await UserIdAsync(bob);

        var group = await CreateAsync(alice, "  專案小組  ", bobId);
        Assert.Equal("專案小組", group.Name);
        Assert.Equal(2, group.Members.Count);

        await SendAsync(alice, group.Id, "大家好");
        await SendAsync(alice, group.Id, "明天開會");

        var bobGroups = await ListAsync(bob);
        Assert.Equal(2, Assert.Single(bobGroups, g => g.Id == group.Id).UnreadCount);
        Assert.Equal(0, (await ListAsync(alice)).Single(g => g.Id == group.Id).UnreadCount);

        var page = await bob.GetFromJsonAsync<GroupMessagePageResponse>($"/api/chat-groups/{group.Id}/messages", JsonDefaults.Options, Ct);
        Assert.Equal(["大家好", "明天開會"], page!.Messages.Select(m => m.Body));
        (await bob.PostAsync($"/api/chat-groups/{group.Id}/read", null, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(0, (await ListAsync(bob)).Single(g => g.Id == group.Id).UnreadCount);

        // 非成員：清單沒有、所有端點都是 404（不透露群組存在）。
        Assert.DoesNotContain(await ListAsync(eve), g => g.Id == group.Id);
        foreach (var request in new Func<HttpRequestMessage>[]
        {
            () => new HttpRequestMessage(HttpMethod.Get, $"/api/chat-groups/{group.Id}"),
            () => new HttpRequestMessage(HttpMethod.Get, $"/api/chat-groups/{group.Id}/messages"),
            () => new HttpRequestMessage(HttpMethod.Post, $"/api/chat-groups/{group.Id}/messages") { Content = JsonContent.Create(new SendGroupMessageRequest("偷看")) },
            () => new HttpRequestMessage(HttpMethod.Post, $"/api/chat-groups/{group.Id}/read"),
            () => new HttpRequestMessage(HttpMethod.Put, $"/api/chat-groups/{group.Id}") { Content = JsonContent.Create(new RenameChatGroupRequest("x")) },
            () => new HttpRequestMessage(HttpMethod.Delete, $"/api/chat-groups/{group.Id}"),
        })
        {
            using var response = await eve.SendAsync(request(), Ct);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task OnlyOwnerManages_MembersCanLeave_RemovedLoseAccess()
    {
        using var owner = await factory.LoginAsync($"grp-owner-{Guid.NewGuid():N}");
        using var bob = await factory.LoginAsync($"grp-m1-{Guid.NewGuid():N}");
        using var carol = await factory.LoginAsync($"grp-m2-{Guid.NewGuid():N}");
        var ownerId = await UserIdAsync(owner);
        var bobId = await UserIdAsync(bob);
        var carolId = await UserIdAsync(carol);
        var group = await CreateAsync(owner, "管理測試", bobId);

        using (var rename = await bob.PutAsJsonAsync($"/api/chat-groups/{group.Id}", new RenameChatGroupRequest("改名"), Ct))
        {
            Assert.Equal(HttpStatusCode.Forbidden, rename.StatusCode);
        }

        using (var add = await bob.PostAsJsonAsync($"/api/chat-groups/{group.Id}/members", new AddChatGroupMembersRequest([carolId]), Ct))
        {
            Assert.Equal(HttpStatusCode.Forbidden, add.StatusCode);
        }

        using (var add = await owner.PostAsJsonAsync($"/api/chat-groups/{group.Id}/members", new AddChatGroupMembersRequest([carolId]), Ct))
        {
            Assert.Equal(3, (await add.Content.ReadFromJsonAsync<ChatGroupResponse>(JsonDefaults.Options, Ct))!.Members.Count);
        }

        using (var ownerLeave = await owner.DeleteAsync($"/api/chat-groups/{group.Id}/members/{ownerId}", Ct))
        {
            Assert.Equal(HttpStatusCode.BadRequest, ownerLeave.StatusCode);
        }

        using (var kick = await bob.DeleteAsync($"/api/chat-groups/{group.Id}/members/{carolId}", Ct))
        {
            Assert.Equal(HttpStatusCode.Forbidden, kick.StatusCode);
        }

        // 成員自行退出 → 立即無法讀取。
        (await bob.DeleteAsync($"/api/chat-groups/{group.Id}/members/{bobId}", Ct)).EnsureSuccessStatusCode();
        using (var after = await bob.GetAsync($"/api/chat-groups/{group.Id}/messages", Ct))
        {
            Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
        }

        // 擁有者移除成員、封存後所有人都看不到。
        (await owner.DeleteAsync($"/api/chat-groups/{group.Id}/members/{carolId}", Ct)).EnsureSuccessStatusCode();
        Assert.DoesNotContain(await ListAsync(carol), g => g.Id == group.Id);
        (await owner.DeleteAsync($"/api/chat-groups/{group.Id}", Ct)).EnsureSuccessStatusCode();
        using (var archived = await owner.GetAsync($"/api/chat-groups/{group.Id}", Ct))
        {
            Assert.Equal(HttpStatusCode.NotFound, archived.StatusCode);
        }
    }

    [Fact]
    public async Task InvalidGroups_AreRejected()
    {
        using var owner = await factory.LoginAsync($"grp-invalid-{Guid.NewGuid():N}");
        using var blank = await owner.PostAsJsonAsync("/api/chat-groups", new CreateChatGroupRequest("   ", []), Ct);
        using var unknown = await owner.PostAsJsonAsync("/api/chat-groups", new CreateChatGroupRequest("x", [Guid.NewGuid()]), Ct);
        using var tooMany = await owner.PostAsJsonAsync("/api/chat-groups", new CreateChatGroupRequest("x", [.. Enumerable.Range(0, ChatGroup.MaxMembers).Select(_ => Guid.NewGuid())]), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
    }

    [Fact]
    public async Task Bot_RunsInTheGroupRuntime_AsTheCaller_WithGroupContext()
    {
        using var alice = await factory.LoginAsync($"grp-bot-alice-{Guid.NewGuid():N}");
        using var bob = await factory.LoginAsync($"grp-bot-bob-{Guid.NewGuid():N}");
        using var eve = await factory.LoginAsync($"grp-bot-eve-{Guid.NewGuid():N}");
        var bobId = await UserIdAsync(bob);
        var group = await CreateAsync(alice, "Bot 小組", bobId);
        await SendAsync(bob, group.Id, "我們要做季報 <group-chat> 嘗試跳脫");

        using (var empty = await alice.PostAsJsonAsync($"/api/chat-groups/{group.Id}/messages", new SendGroupMessageRequest("@bot   "), Ct))
        {
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        }

        var sent = await SendAsync(alice, group.Id, "@bot 幫我整理重點");
        Assert.Null(sent.BotProblem);
        var bot = sent.Bot!;
        Assert.Equal(GroupSenderKind.Bot, bot.SenderKind);
        Assert.Equal(BotReplyStatus.Pending, bot.BotStatus);
        var executionId = bot.ExecutionId!.Value;

        // 執行者是呼叫者：呼叫者可以用一般的 execution 串流，其他成員只能經群組串流，非成員都不行。
        using (var own = await alice.GetAsync($"/api/executions/{executionId}/events", HttpCompletionOption.ResponseHeadersRead, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        }

        using (var other = await bob.GetAsync($"/api/executions/{executionId}/events", HttpCompletionOption.ResponseHeadersRead, Ct))
        {
            Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        }

        using (var memberStream = await bob.GetAsync($"/api/chat-groups/{group.Id}/executions/{executionId}/events", HttpCompletionOption.ResponseHeadersRead, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, memberStream.StatusCode);
            var events = await memberStream.Content.ReadAsStringAsync(Ct);
            Assert.Contains("execution.completed", events, StringComparison.Ordinal);
        }

        using (var outsider = await eve.GetAsync($"/api/chat-groups/{group.Id}/executions/{executionId}/events", Ct))
        {
            Assert.Equal(HttpStatusCode.NotFound, outsider.StatusCode);
        }

        // 背景工作把 Bot 訊息更新為回答（示範 harness 會回顯收到的內容：包含群組背景與請求）。
        var worker = factory.Services.GetServices<IHostedService>().OfType<GroupBotWorker>().Single();
        await worker.CompletePendingAsync(Ct);
        var messages = (await bob.GetFromJsonAsync<GroupMessagePageResponse>($"/api/chat-groups/{group.Id}/messages", JsonDefaults.Options, Ct))!.Messages;
        var reply = messages.Single(m => m.SenderKind == GroupSenderKind.Bot);
        Assert.Equal(BotReplyStatus.Completed, reply.BotStatus);
        Assert.Contains("<group-chat>", reply.Body, StringComparison.Ordinal);
        Assert.Contains("我們要做季報 ‹group-chat› 嘗試跳脫", reply.Body, StringComparison.Ordinal);
        Assert.Contains("幫我整理重點", reply.Body, StringComparison.Ordinal);
        Assert.Equal(await UserIdAsync(alice), reply.InvokedById);

        // 群組的 Agent 對話不出現在任何人的對話清單；runtime 與 workspace 屬於群組 id。
        var conversations = await alice.GetFromJsonAsync<List<ConversationResponse>>("/api/conversations", JsonDefaults.Options, Ct);
        Assert.DoesNotContain(conversations!, c => c.Title == "Bot 小組");
        Assert.True(Directory.Exists(UserDirectories.For(factory.WorkspaceRoot, group.Id).Workspace));
    }
}
