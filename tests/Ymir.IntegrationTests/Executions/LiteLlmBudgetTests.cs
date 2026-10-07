using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.PiAgent;
using Ymir.Platform.Users;
using Ymir.Testing.FakeLlm;
using Ymir.VibeMaker.Contracts.Executions;

namespace Ymir.IntegrationTests.Executions;

/// <summary>獨立的 fixture（設定同 <see cref="LiteLlmPiApiFactory"/>）：這組測試會修改執行政策（每月預算），不影響其他 LiteLLM 測試。</summary>
public sealed class LiteLlmBudgetApiFactory : ApiFactory
{
    private readonly Lazy<FakeLlmServer> _liteLlm = new(() => FakeLlmServer.StartAsync(masterKey: LiteLlmPiApiFactory.MasterKey).GetAwaiter().GetResult());

    public FakeLlmState LiteLlm => _liteLlm.Value.State;

    protected override void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting("VibeMaker:Harness", "Pi");
        builder.UseSetting("VibeMaker:Pi:ModelBaseUrl", _liteLlm.Value.BaseUrl.ToString());
        builder.UseSetting("VibeMaker:Pi:ModelId", FakeLlmEndpoints.ModelId);
        builder.UseSetting("VibeMaker:Pi:AutoRetry", "false");
        builder.UseSetting("VibeMaker:LiteLlm:BaseUrl", _liteLlm.Value.RootUrl.ToString());
        builder.UseSetting("VibeMaker:LiteLlm:MasterKey", LiteLlmPiApiFactory.MasterKey);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_liteLlm.IsValueCreated)
        {
            await _liteLlm.Value.DisposeAsync();
        }
    }
}

/// <summary>
/// ADR-0011：key 掛在 LiteLLM 使用者底下、每人每月預算由 LiteLLM 強制、管理介面的用量讀 LiteLLM 的花費與 token。
/// LiteLLM 由 Fake LLM 模擬（每次呼叫 10 + 5 token、US$0.01）。
/// </summary>
public class LiteLlmBudgetTests(LiteLlmBudgetApiFactory factory) : IClassFixture<LiteLlmBudgetApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task SavePolicyAsync(HttpClient admin, decimal monthlyBudget)
    {
        using var response = await admin.PutAsJsonAsync("/api/admin/settings/runtime", new SaveRuntimePolicyRequest(30, 30, 5, 0, monthlyBudget), Ct);
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Budget_IsAppliedToTheLiteLlmUser_UsageIsReported_AndExhaustionFailsWithASummary()
    {
        Assert.SkipUnless(PiHarnessFixture.IsPiOnPath(), "pi is not on PATH (npm i -g @earendil-works/pi-coding-agent@1.0.0)");
        using var admin = await factory.LoginAsync($"budget-admin-{Guid.NewGuid():N}", UserRole.Admin);
        await SavePolicyAsync(admin, 0.02m);
        try
        {
            using var client = await factory.LoginAsync($"budget-user-{Guid.NewGuid():N}");
            var userId = (await client.GetFromJsonAsync<JsonElement>("/api/me", Ct)).GetProperty("id").GetGuid().ToString("D");
            var conversation = await client.CreateConversationAsync(null, "budget");

            // 第一次執行：LiteLLM 使用者帶著預算建立，key 掛在使用者底下
            var (_, first) = await client.SendMessageAsync(conversation.Id, "hi");
            var firstEvents = await client.ReadEventsAsync(first!.EventStreamUrl);
            Assert.Equal(ExecutionEventNames.ExecutionCompleted, firstEvents[^1].EventType);
            var liteLlmUser = factory.LiteLlm.LiteLlmUsers.Find(userId);
            Assert.NotNull(liteLlmUser);
            Assert.Equal(0.02m, liteLlmUser.MaxBudget);
            Assert.Equal("30d", liteLlmUser.BudgetDuration);
            Assert.Contains(factory.LiteLlm.GenerateRequests, r => r["user_id"]?.GetValue<string>() == userId);

            // 用量頁讀到 LiteLLM 的花費與 token
            var usage = await admin.GetFromJsonAsync<AdminUsageResponse>("/api/admin/usage?days=7", JsonDefaults.Options, Ct);
            Assert.True(usage!.ModelUsageAvailable);
            Assert.Equal(0.02m, usage.MonthlyBudgetUsd);
            var row = Assert.Single(usage.Users, u => u.UserId.ToString("D") == userId);
            Assert.True(row.SpendUsd > 0);
            Assert.True(row.PromptTokens >= FakeLiteLlmUsers.PromptTokensPerRequest);
            Assert.True(row.CompletionTokens >= FakeLiteLlmUsers.CompletionTokensPerRequest);
            Assert.True(row.ModelRequests >= 1);
            Assert.Equal(0.02m, row.BudgetUsd);

            // 繼續使用直到預算用完：LiteLLM 拒絕呼叫，execution 以預算摘要結束（不含原始錯誤）
            JsonElement? failed = null;
            for (var i = 0; i < 5 && failed is null; i++)
            {
                var (response, sent) = await client.SendMessageAsync(conversation.Id, $"again {i}");
                if (sent is null)
                {
                    Assert.Equal(System.Net.HttpStatusCode.TooManyRequests, response.StatusCode); // 送出前的預算檢查
                    break;
                }

                var events = await client.ReadEventsAsync(sent.EventStreamUrl);
                if (events[^1].EventType == ExecutionEventNames.ExecutionFailed)
                {
                    failed = events[^1].Data;
                }
            }

            Assert.True(liteLlmUser.IsOverBudget);
            if (failed is { } data)
            {
                Assert.Contains("預算", data.GetProperty("message").GetString(), StringComparison.Ordinal);
                Assert.DoesNotContain("Max budget", data.GetRawText(), StringComparison.Ordinal);
            }

            // 管理介面調高預算：立即套用到既有的 LiteLLM 使用者
            await SavePolicyAsync(admin, 5m);
            Assert.Equal(5m, liteLlmUser.MaxBudget);
        }
        finally
        {
            await admin.DeleteAsync("/api/admin/settings/runtime", Ct);
        }
    }
}
