using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.Executions;
using Ymir.IntegrationTests.Knowledge;
using Ymir.Platform.Users;
using Ymir.Testing.FakeLlm;
using Ymir.VibeMaker.Application.Admin;
using Ymir.VibeMaker.Contracts.Executions;

namespace Ymir.IntegrationTests.Admin;

/// <summary>
/// Scripted harness + Fake LLM 模擬的 LiteLLM（master key）：Scripted 執行仍會發 virtual key，所以 LiteLLM 使用者存在，
/// 測試直接在 Fake LLM 準備每個模型的用量，不需要 Pi。知識庫的 embedding 經使用者的 key 呼叫 Fake LLM（ADR-0014）。
/// </summary>
public sealed class ModelUsageApiFactory : ApiFactory
{
    public const string EmbeddingModel = "fake-embedding";

    private readonly Lazy<FakeLlmServer> _liteLlm = new(() => FakeLlmServer.StartAsync(masterKey: LiteLlmPiApiFactory.MasterKey).GetAwaiter().GetResult());

    public string KnowledgeRoot { get; } = Path.Combine(Path.GetTempPath(), "ymir-model-usage-" + Guid.NewGuid().ToString("N"));

    public FakeLlmState LiteLlm => _liteLlm.Value.State;

    protected override void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting("VibeMaker:Pi:ModelId", FakeLlmEndpoints.ModelId);
        builder.UseSetting("VibeMaker:Pi:ModelBaseUrl", _liteLlm.Value.BaseUrl.ToString());
        builder.UseSetting("VibeMaker:Models:0:Id", FakeLlmEndpoints.ModelId);
        builder.UseSetting("VibeMaker:Models:0:DisplayName", "Fake");
        builder.UseSetting("VibeMaker:Models:1:Id", "fake-model-2");
        builder.UseSetting("VibeMaker:Models:1:DisplayName", "Fake 2");
        builder.UseSetting("VibeMaker:LiteLlm:BaseUrl", _liteLlm.Value.RootUrl.ToString());
        builder.UseSetting("VibeMaker:LiteLlm:MasterKey", LiteLlmPiApiFactory.MasterKey);
        builder.UseSetting("VibeMaker:Rag:EmbeddingModel", EmbeddingModel);
        builder.UseSetting("VibeMaker:Rag:PollInterval", "00:00:00.500");
        builder.UseSetting("Ymir:Knowledge:Root", KnowledgeRoot);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_liteLlm.IsValueCreated)
        {
            await _liteLlm.Value.DisposeAsync();
        }

        if (Directory.Exists(KnowledgeRoot))
        {
            Directory.Delete(KnowledgeRoot, recursive: true);
        }
    }
}

/// <summary>ADR-0021：管理介面的用量依模型拆分，執行數來自 Ymir，請求、token 與費用來自 LiteLLM 的 model_groups。</summary>
public class ModelUsageTests(ModelUsageApiFactory factory) : IClassFixture<ModelUsageApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> UserIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/me", Ct)).GetProperty("id").GetGuid();

    private static async Task RunAsync(HttpClient client, Guid conversationId, string content, string? modelId = null)
    {
        var (_, sent) = await client.SendMessageAsync(conversationId, content, modelId: modelId);
        var events = await client.ReadEventsAsync(sent!.EventStreamUrl);
        Assert.Equal(ExecutionEventNames.ExecutionCompleted, events[^1].EventType);
    }

    private FakeLiteLlmUser LiteLlmUser(Guid userId)
    {
        var user = factory.LiteLlm.LiteLlmUsers.Find(userId.ToString("D"));
        Assert.NotNull(user); // Scripted 執行仍會發 virtual key，LiteLLM 使用者因此已建立
        return user;
    }

    [Fact]
    public async Task Usage_BreaksDownEachUsersExecutionsAndLiteLlmUsageByModel()
    {
        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);

        using var a = await factory.LoginAsync($"model-usage-a-{Guid.NewGuid():N}");
        var aId = await UserIdAsync(a);
        var aConversation = await a.CreateConversationAsync(null, "model usage a");
        await RunAsync(a, aConversation.Id, "hi");
        await RunAsync(a, aConversation.Id, "again", modelId: "fake-model-2");
        var aLiteLlm = LiteLlmUser(aId);
        aLiteLlm.RecordRequest(FakeLlmEndpoints.ModelId);
        aLiteLlm.RecordRequest(FakeLlmEndpoints.ModelId);
        aLiteLlm.RecordRequest("fake-model-2");
        aLiteLlm.RecordRequest(ModelUsageApiFactory.EmbeddingModel, promptTokens: 16, completionTokens: 0, cost: 0m);
        aLiteLlm.RecordRequest("retired-model");
        aLiteLlm.RecordRequest(string.Empty); // LiteLLM 沒有標示 model group 的紀錄
        aLiteLlm.RecordRequest(FakeLlmEndpoints.ModelId, date: todayUtc.AddDays(-7)); // 超出查詢期間

        using var b = await factory.LoginAsync($"model-usage-b-{Guid.NewGuid():N}");
        var bId = await UserIdAsync(b);
        var bConversation = await b.CreateConversationAsync(null, "model usage b");
        await RunAsync(b, bConversation.Id, "hello");
        var bLiteLlm = LiteLlmUser(bId);
        for (var i = 0; i < 3; i++)
        {
            bLiteLlm.RecordRequest(FakeLlmEndpoints.ModelId);
        }

        using var admin = await factory.LoginAsync($"model-usage-admin-{Guid.NewGuid():N}", UserRole.Admin);
        var usage = await admin.GetFromJsonAsync<AdminUsageResponse>("/api/admin/usage?days=7", JsonDefaults.Options, Ct);
        Assert.True(usage!.ModelUsageAvailable);

        var aRow = Assert.Single(usage.Users, u => u.UserId == aId);
        Assert.Equal([FakeLlmEndpoints.ModelId, "fake-model-2", "retired-model", ModelUsageApiFactory.EmbeddingModel, null], aRow.Models.Select(m => m.ModelId));

        var fake = aRow.Models[0];
        Assert.Equal((ModelUsageKind.Catalog, "Fake", 1, 2L), (fake.Kind, fake.DisplayName, fake.Executions, fake.ModelRequests));
        Assert.Equal((20L, 10L, 0.02m), (fake.PromptTokens, fake.CompletionTokens, fake.SpendUsd));

        var fake2 = aRow.Models[1];
        Assert.Equal((ModelUsageKind.Catalog, "Fake 2", 1, 1L), (fake2.Kind, fake2.DisplayName, fake2.Executions, fake2.ModelRequests));

        var retired = aRow.Models[2];
        Assert.Equal((ModelUsageKind.Unlisted, "retired-model", 0), (retired.Kind, retired.DisplayName, retired.Executions));

        var embedding = aRow.Models[3];
        Assert.Equal((ModelUsageKind.Embedding, 0), (embedding.Kind, embedding.Executions));
        Assert.Equal((16L, 0L, 0m), (embedding.PromptTokens, embedding.CompletionTokens, embedding.SpendUsd));

        var unattributed = aRow.Models[4];
        Assert.Equal((ModelUsageKind.Unattributed, "（未標示模型）", 1L), (unattributed.Kind, unattributed.DisplayName, unattributed.ModelRequests));

        // 各列加總等於使用者的總計（ADR-0021）
        Assert.Equal(6L, aRow.ModelRequests);
        Assert.Equal(aRow.ModelRequests, aRow.Models.Sum(m => m.ModelRequests));
        Assert.Equal(0.05m, aRow.SpendUsd);
        Assert.Equal(aRow.SpendUsd, aRow.Models.Sum(m => m.SpendUsd));
        Assert.Equal(2, aRow.Executions);
        Assert.Equal(aRow.Executions, aRow.Models.Sum(m => m.Executions));

        // 每位使用者只看到自己的用量
        var bRow = Assert.Single(usage.Users, u => u.UserId == bId);
        var bModel = Assert.Single(bRow.Models);
        Assert.Equal((FakeLlmEndpoints.ModelId, 3L, 1), (bModel.ModelId, bModel.ModelRequests, bModel.Executions));
    }

    [Fact]
    public async Task Usage_ListsKnowledgeOnlyUsers_WithTheirEmbeddingUsage()
    {
        using var c = await factory.LoginAsync($"model-usage-kb-{Guid.NewGuid():N}");
        var cId = await UserIdAsync(c);
        var project = await c.CreateProjectAsync("model usage kb");
        var document = await KnowledgeBaseTests.UploadReadyAsync(c, project.Id, "notes.txt", "第一條：每年有十四天特別休假。\n\n第二條：加班需事先申請。");
        Assert.Equal(Ymir.VibeMaker.Domain.KnowledgeDocumentStatus.Ready, document.Status);

        using var admin = await factory.LoginAsync($"model-usage-kb-admin-{Guid.NewGuid():N}", UserRole.Admin);
        var usage = await admin.GetFromJsonAsync<AdminUsageResponse>("/api/admin/usage?days=7", JsonDefaults.Options, Ct);

        // 只用知識庫的使用者沒有 runtime，也沒有執行，仍以知識庫文件的擁有者列出
        var row = Assert.Single(usage!.Users, u => u.UserId == cId);
        Assert.Equal(0, row.Executions);
        var model = Assert.Single(row.Models, m => m.Kind == ModelUsageKind.Embedding);
        Assert.Equal(ModelUsageApiFactory.EmbeddingModel, model.ModelId);
        Assert.True(model.ModelRequests >= 1);
        Assert.Equal(0m, model.SpendUsd);
    }
}
