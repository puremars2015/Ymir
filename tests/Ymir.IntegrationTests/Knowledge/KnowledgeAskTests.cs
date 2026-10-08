using System.Net;
using System.Net.Http.Json;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.Platform.Users;
using Ymir.Testing.FakeLlm;
using Ymir.VibeMaker.Contracts.Models;

namespace Ymir.IntegrationTests.Knowledge;

/// <summary>知識庫問答（ADR-0014 §8）：檢索、引用、資料不足、模型允許清單、專案隔離。</summary>
public class KnowledgeAskTests(KnowledgeApiFactory factory) : IClassFixture<KnowledgeApiFactory>
{
    [Fact]
    public async Task UserDeniedModel_CannotSendKnowledgePassagesToIt()
    {
        using var client = await factory.LoginAsync($"kb-denied-{Guid.NewGuid():N}");
        using var admin = await factory.LoginAsync($"kb-admin-{Guid.NewGuid():N}", UserRole.Admin);
        var me = (await client.GetFromJsonAsync<MeResponse>("/api/me", JsonDefaults.Options, Ct))!;
        var project = await client.CreateProjectAsync("個人模型權限");
        await KnowledgeBaseTests.UploadReadyAsync(client, project.Id, "policy.txt", "年度預算為五十萬元，需要主管核准。");
        using var saved = await admin.PutAsJsonAsync($"/api/admin/users/{me.Id}/models",
            new SaveUserModelAccessRequest(new Dictionary<string, bool> { [FakeLlmEndpoints.ModelId] = false }), Ct);
        saved.EnsureSuccessStatusCode();
        var before = factory.FakeLlm.Requests.Count;
        using var response = await AskAsync(client, project.Id, "年度預算多少？", FakeLlmEndpoints.ModelId);
        response.EnsureSuccessStatusCode();
        var answer = (await response.Content.ReadFromJsonAsync<KnowledgeAnswerResponse>(JsonDefaults.Options, Ct))!;
        Assert.False(answer.ModelAllowed);
        Assert.Null(answer.Answer);
        Assert.NotEmpty(answer.Citations);
        Assert.Equal(before, factory.FakeLlm.Requests.Count);
    }
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<HttpResponseMessage> AskAsync(HttpClient client, Guid projectId, string question, string? modelId = null) =>
        await client.PostAsJsonAsync($"/api/projects/{projectId}/knowledge/ask", new AskKnowledgeRequest(question, modelId), JsonDefaults.Options, Ct);

    [Fact]
    public async Task Answers_CiteTheMatchingDocument()
    {
        using var client = await factory.LoginAsync($"kb-ask-{Guid.NewGuid():N}");
        var project = await client.CreateProjectAsync("問答");
        await KnowledgeBaseTests.UploadReadyAsync(client, project.Id, "請假規則.txt", "特別休假每年十四天，需要提前三天在系統申請。");
        await KnowledgeBaseTests.UploadReadyAsync(client, project.Id, "停車.txt", "員工停車場在地下二樓，訪客請登記車牌。");
        var requestsBefore = factory.FakeLlm.Requests.Count;

        using var response = await AskAsync(client, project.Id, "特別休假要提前幾天申請？");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var answer = (await response.Content.ReadFromJsonAsync<KnowledgeAnswerResponse>(JsonDefaults.Options, Ct))!;
        Assert.False(answer.InsufficientData);
        Assert.True(answer.ModelAllowed);
        Assert.StartsWith(FakeLlmScript.KnowledgeReplyPrefix, answer.Answer, StringComparison.Ordinal);
        Assert.Contains("[1]", answer.Answer, StringComparison.Ordinal);
        var first = answer.Citations[0];
        Assert.Equal(1, first.Number);
        Assert.Equal("請假規則.txt", first.FileName);
        Assert.Contains("十四天", first.Excerpt, StringComparison.Ordinal);
        // 片段放在資料區塊，送給模型的是 system 規則 + 片段 + 問題。
        var sent = factory.FakeLlm.Requests.Skip(requestsBefore).Single(r => r["messages"] is not null);
        Assert.Contains("<knowledge>", sent["messages"]![1]!["content"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnrelatedQuestionsOrEmptyKnowledge_AreInsufficient_WithoutCallingTheModel()
    {
        using var client = await factory.LoginAsync($"kb-insufficient-{Guid.NewGuid():N}");
        var empty = await client.CreateProjectAsync("空的");
        var project = await client.CreateProjectAsync("有資料");
        await KnowledgeBaseTests.UploadReadyAsync(client, project.Id, "menu.txt", "午餐菜單：星期一咖哩飯。");
        var requestsBefore = factory.FakeLlm.Requests.Count;

        using var none = await AskAsync(client, empty.Id, "午餐吃什麼？");
        using var unrelated = await AskAsync(client, project.Id, "zzzz qqqq xxxx");

        Assert.True((await none.Content.ReadFromJsonAsync<KnowledgeAnswerResponse>(JsonDefaults.Options, Ct))!.InsufficientData);
        var answer = (await unrelated.Content.ReadFromJsonAsync<KnowledgeAnswerResponse>(JsonDefaults.Options, Ct))!;
        Assert.True(answer.InsufficientData);
        Assert.Null(answer.Answer);
        Assert.Empty(answer.Citations);
        Assert.Equal(requestsBefore, factory.FakeLlm.Requests.Count);
    }

    [Fact]
    public async Task ModelsNotAllowedForKnowledge_OnlyGetRetrievedPassages()
    {
        using var client = await factory.LoginAsync($"kb-restricted-{Guid.NewGuid():N}");
        var project = await client.CreateProjectAsync("受限");
        await KnowledgeBaseTests.UploadReadyAsync(client, project.Id, "secret.txt", "機密資料：明年預算三千萬。");
        var requestsBefore = factory.FakeLlm.Requests.Count;

        using var response = await AskAsync(client, project.Id, "明年預算多少？", KnowledgeApiFactory.RestrictedModel);

        var answer = (await response.Content.ReadFromJsonAsync<KnowledgeAnswerResponse>(JsonDefaults.Options, Ct))!;
        Assert.False(answer.ModelAllowed);
        Assert.Null(answer.Answer);
        Assert.NotEmpty(answer.Citations);
        // 片段沒有送給未獲允許的模型（ADR-0014 §8 資料外送政策）。
        Assert.Equal(requestsBefore, factory.FakeLlm.Requests.Count);
    }

    [Fact]
    public async Task OtherUsersAndInvalidQuestions_AreRejected()
    {
        using var alice = await factory.LoginAsync($"kb-ask-alice-{Guid.NewGuid():N}");
        using var bob = await factory.LoginAsync($"kb-ask-bob-{Guid.NewGuid():N}");
        var project = await alice.CreateProjectAsync("alice");
        await KnowledgeBaseTests.UploadReadyAsync(alice, project.Id, "a.txt", "藍色計畫的代號是 Ymir。");

        using var intrusion = await AskAsync(bob, project.Id, "藍色計畫的代號？");
        using var blank = await AskAsync(alice, project.Id, "   ");
        using var tooLong = await AskAsync(alice, project.Id, new string('問', 2001));

        Assert.Equal(HttpStatusCode.NotFound, intrusion.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }
}
