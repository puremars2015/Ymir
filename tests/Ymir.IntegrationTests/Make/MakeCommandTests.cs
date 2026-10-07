using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.Executions;
using Ymir.IntegrationTests.PiAgent;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Contracts.Conversations;
using Ymir.VibeMaker.Contracts.Executions;
using Ymir.VibeMaker.Contracts.Make;

namespace Ymir.IntegrationTests.Make;

/// <summary><c>/make</c>：主題按鈕、依描述判斷主題、Admin 管理主題（真實 Pi + Fake LLM 檢查送到模型的內容）。</summary>
public class MakeCommandTests(MakeApiFactory factory) : IClassFixture<MakeApiFactory>
{
    private static readonly Guid SmallToolTopicId = Guid.Parse("0199b7a0-0000-7000-8000-000000000001");

    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("code").GetString()!;

    /// <summary>Fake LLM 最後一次收到的 user 訊息內容。</summary>
    private string LastUserMessageToModel()
    {
        var content = factory.FakeLlm.LastRequest!["messages"]!.AsArray()
            .Last(m => m!["role"]!.GetValue<string>() == "user")!["content"]!;
        // OpenAI 格式的 content 可能是字串，或是 [{ type: "text", text }] 陣列。
        return content is System.Text.Json.Nodes.JsonArray parts
            ? string.Concat(parts.Select(p => p?["text"]?.GetValue<string>()))
            : content.GetValue<string>();
    }

    [Fact]
    public async Task DefaultTopics_AreSeeded_InOrder()
    {
        using var client = await factory.LoginAsync("make-list");

        var topics = await client.GetFromJsonAsync<List<MakeTopicResponse>>("/api/make-topics", JsonDefaults.Options, TestContext.Current.CancellationToken);

        Assert.Equal(["小工具架設", "網站系統架設", "建立簡報", "建立公告 Word"], topics!.Take(4).Select(t => t.Name));
    }

    [Fact]
    public async Task DocumentTopics_IncludeGuidanceAndPlatformTemplate()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await factory.LoginAsync("make-document-admin", UserRole.Admin);
        var topics = await admin.GetFromJsonAsync<List<AdminMakeTopicResponse>>("/api/admin/make-topics", JsonDefaults.Options, ct);
        var presentation = Assert.Single(topics!, t => t.Id == Guid.Parse("0199b7a0-0000-7000-8000-000000000003"));
        var announcement = Assert.Single(topics!, t => t.Id == Guid.Parse("0199b7a0-0000-7000-8000-000000000004"));
        Assert.True(presentation.IsEnabled);
        Assert.True(announcement.IsEnabled);
        Assert.Contains("逐頁大綱", presentation.Instructions, StringComparison.Ordinal);
        Assert.Contains(".pptx", presentation.Instructions, StringComparison.Ordinal);
        Assert.Contains("/opt/ymir/templates/announcement/template.docx", announcement.Instructions, StringComparison.Ordinal);
        Assert.Contains("build.py", announcement.Instructions, StringComparison.Ordinal);
        Assert.Contains("不交付", announcement.Instructions, StringComparison.Ordinal);
        Assert.Contains("聯絡窗口", announcement.Instructions, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0199b7a0-0000-7000-8000-000000000003", "建立簡報", "逐頁大綱")]
    [InlineData("0199b7a0-0000-7000-8000-000000000004", "建立公告 Word", "build.py")]
    public async Task DocumentTopicButton_AsksQuestionsBeforeCreatingDeliverables(string id, string name, string instruction)
    {
        Assert.SkipUnless(PiHarnessFixture.IsPiOnPath(), "pi is not on PATH");
        using var client = await factory.LoginAsync("make-doc-" + id);
        var conversation = await client.CreateConversationAsync(null, "make documents");
        var (_, sent) = await client.SendMessageAsync(conversation.Id, "/make " + name, makeTopicId: Guid.Parse(id));
        var events = await client.ReadEventsAsync(sent!.EventStreamUrl);
        Assert.Equal(ExecutionEventNames.ExecutionCompleted, events[^1].EventType);
        var prompt = LastUserMessageToModel();
        Assert.Contains(instruction, prompt, StringComparison.Ordinal);
        Assert.Contains("先不要建立任何檔案", prompt, StringComparison.Ordinal);
        Assert.Contains("本次成果目錄", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TopicButton_SendsTopicInstructionsToTheAgent_ButHistoryKeepsTheShortText()
    {
        Assert.SkipUnless(PiHarnessFixture.IsPiOnPath(), "pi is not on PATH (npm i -g @earendil-works/pi-coding-agent@1.0.0)");
        using var client = await factory.LoginAsync("make-topic");
        var conversation = await client.CreateConversationAsync(null, "make");

        var (_, sent) = await client.SendMessageAsync(conversation.Id, "/make 小工具架設", makeTopicId: SmallToolTopicId);
        var events = await client.ReadEventsAsync(sent!.EventStreamUrl);

        Assert.Equal(ExecutionEventNames.ExecutionCompleted, events[^1].EventType);
        var toModel = LastUserMessageToModel();
        Assert.Contains("小工具架設", toModel, StringComparison.Ordinal);
        Assert.Contains("單一 HTML 檔", toModel, StringComparison.Ordinal);
        Assert.Contains("先不要建立任何檔案", toModel, StringComparison.Ordinal);
        var messages = await client.GetMessagesAsync(conversation.Id);
        Assert.Equal("/make 小工具架設", messages.First(m => m.Role == "USER").Content);
    }

    [Fact]
    public async Task MakeWithDescription_LetsTheAgentChooseAmongEnabledTopics()
    {
        Assert.SkipUnless(PiHarnessFixture.IsPiOnPath(), "pi is not on PATH (npm i -g @earendil-works/pi-coding-agent@1.0.0)");
        using var client = await factory.LoginAsync("make-describe");
        var conversation = await client.CreateConversationAsync(null, "make");

        var (_, sent) = await client.SendMessageAsync(conversation.Id, "/make 一個計算機");
        await client.ReadEventsAsync(sent!.EventStreamUrl);

        var toModel = LastUserMessageToModel();
        Assert.Contains("使用者想建置：一個計算機", toModel, StringComparison.Ordinal);
        Assert.Contains("小工具架設", toModel, StringComparison.Ordinal);
        Assert.Contains("網站系統架設", toModel, StringComparison.Ordinal);
        Assert.Contains("主題：<名稱>", toModel, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BareMake_AndUnknownTopic_AreRejected()
    {
        using var client = await factory.LoginAsync("make-reject");
        var conversation = await client.CreateConversationAsync(null, "make");

        var (bare, _) = await client.SendMessageAsync(conversation.Id, "  /make ");
        var (unknown, _) = await client.SendMessageAsync(conversation.Id, "/make 不存在", makeTopicId: Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, bare.StatusCode);
        Assert.Equal(ExecutionErrorCodes.MakeDescriptionRequired, await ProblemCodeAsync(bare));
        Assert.Equal(ExecutionErrorCodes.MakeTopicNotAvailable, await ProblemCodeAsync(unknown));
    }

    [Fact]
    public async Task Admin_ManagesTopics_AndDisabledTopicsDisappear()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await factory.LoginAsync("make-admin", UserRole.Admin);
        using var user = await factory.LoginAsync("make-user");
        await admin.GetAsync(new Uri("/api/me", UriKind.Relative), ct);

        using var created = await admin.PostAsJsonAsync("/api/admin/make-topics", new SaveMakeTopicRequest("  報表自動化 ", "Excel 報表整理", "用 Python 處理 Excel", 30, true), ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var topic = (await created.Content.ReadFromJsonAsync<AdminMakeTopicResponse>(JsonDefaults.Options, ct))!;
        Assert.Equal("報表自動化", topic.Name);

        var visible = await user.GetFromJsonAsync<List<MakeTopicResponse>>("/api/make-topics", JsonDefaults.Options, ct);
        Assert.Contains(visible!, t => t.Id == topic.Id);
        var raw = await user.GetStringAsync(new Uri("/api/make-topics", UriKind.Relative), ct);
        Assert.DoesNotContain("用 Python 處理 Excel", raw, StringComparison.Ordinal); // 建置指示不回給一般使用者

        using var disabled = await admin.PutAsJsonAsync($"/api/admin/make-topics/{topic.Id}", new SaveMakeTopicRequest("報表自動化", "Excel 報表整理", "用 Python 處理 Excel", 30, false), ct);
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        var afterDisable = await user.GetFromJsonAsync<List<MakeTopicResponse>>("/api/make-topics", JsonDefaults.Options, ct);
        Assert.DoesNotContain(afterDisable!, t => t.Id == topic.Id);
        var conversation = await user.CreateConversationAsync(null, "make");
        var (useDisabled, _) = await user.SendMessageAsync(conversation.Id, "/make 報表自動化", makeTopicId: topic.Id);
        Assert.Equal(ExecutionErrorCodes.MakeTopicNotAvailable, await ProblemCodeAsync(useDisabled));

        var all = await admin.GetFromJsonAsync<List<AdminMakeTopicResponse>>("/api/admin/make-topics", JsonDefaults.Options, ct);
        Assert.Contains(all!, t => t.Id == topic.Id && !t.IsEnabled);

        using var deleted = await admin.DeleteAsync(new Uri($"/api/admin/make-topics/{topic.Id}", UriKind.Relative), ct);
        using var deletedAgain = await admin.DeleteAsync(new Uri($"/api/admin/make-topics/{topic.Id}", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deletedAgain.StatusCode);
    }

    [Fact]
    public async Task Admin_InvalidTopic_Returns400()
    {
        using var admin = await factory.LoginAsync("make-admin-invalid", UserRole.Admin);
        await admin.GetAsync(new Uri("/api/me", UriKind.Relative), TestContext.Current.CancellationToken);

        using var response = await admin.PostAsJsonAsync("/api/admin/make-topics", new SaveMakeTopicRequest(" ", null, "x", 0, true), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(response));
    }
}
