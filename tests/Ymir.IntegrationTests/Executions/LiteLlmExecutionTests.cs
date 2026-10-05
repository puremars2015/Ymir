using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.PiAgent;
using Ymir.Testing.FakeLlm;
using Ymir.VibeMaker.Contracts.Executions;

namespace Ymir.IntegrationTests.Executions;

/// <summary>
/// ADR-0004：API 以 master key 向 LiteLLM（這裡是 Fake LLM 模擬的 key management）為使用者發 virtual key，
/// Pi 只用 virtual key 呼叫模型。Fake LLM 在此模式下會拒絕 master key 與未發放的 key。
/// </summary>
public sealed class LiteLlmPiApiFactory : ApiFactory
{
    public const string MasterKey = "sk-it-master-key";

    private readonly Lazy<FakeLlmServer> _liteLlm = new(() => FakeLlmServer.StartAsync(masterKey: MasterKey).GetAwaiter().GetResult());

    public FakeLlmState LiteLlm => _liteLlm.Value.State;

    protected override void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting("VibeMaker:Harness", "Pi");
        builder.UseSetting("VibeMaker:Pi:ModelBaseUrl", _liteLlm.Value.BaseUrl.ToString());
        builder.UseSetting("VibeMaker:Pi:ModelId", FakeLlmEndpoints.ModelId);
        builder.UseSetting("VibeMaker:Pi:AutoRetry", "false");
        builder.UseSetting("VibeMaker:LiteLlm:BaseUrl", _liteLlm.Value.RootUrl.ToString());
        builder.UseSetting("VibeMaker:LiteLlm:MasterKey", MasterKey);
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

/// <summary>LiteLLM 無法連線：execution 必須以失敗結束，不能卡在執行中（SA §14）。</summary>
public sealed class UnreachableLiteLlmApiFactory : ApiFactory
{
    protected override void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting("VibeMaker:Harness", "Pi");
        builder.UseSetting("VibeMaker:Pi:ModelId", FakeLlmEndpoints.ModelId);
        // port 9（discard）沒有服務在聽，連線會立即被拒絕。
        builder.UseSetting("VibeMaker:LiteLlm:BaseUrl", "http://127.0.0.1:9/");
        builder.UseSetting("VibeMaker:LiteLlm:MasterKey", "sk-unused");
    }
}

public class LiteLlmExecutionTests(LiteLlmPiApiFactory factory) : IClassFixture<LiteLlmPiApiFactory>
{
    [Fact]
    public async Task Agent_UsesPerUserVirtualKey_NeverTheMasterKey()
    {
        Assert.SkipUnless(PiHarnessFixture.IsPiOnPath(), "pi is not on PATH (npm i -g @earendil-works/pi-coding-agent@1.0.0)");
        var ct = TestContext.Current.CancellationToken;
        using var client = await factory.LoginAsync("litellm-user");
        var me = (await client.GetFromJsonAsync<MeResponse>("/api/me", JsonDefaults.Options, ct))!;
        var conversation = await client.CreateConversationAsync(null, "litellm chat");
        var issuedBefore = factory.LiteLlm.GenerateRequests.Count;

        var (_, first) = await client.SendMessageAsync(conversation.Id, $"{FakeLlmScript.CreateFileMarker} 建立檔案");
        var firstEvents = await client.ReadEventsAsync(first!.EventStreamUrl);
        var (_, second) = await client.SendMessageAsync(conversation.Id, "剛剛做了什麼？");
        var secondEvents = await client.ReadEventsAsync(second!.EventStreamUrl);

        Assert.Equal(ExecutionEventNames.ExecutionCompleted, firstEvents[^1].EventType);
        Assert.Equal(ExecutionEventNames.ExecutionCompleted, secondEvents[^1].EventType);

        // 同一使用者兩次執行共用一把 key（快取），且只限定 Pi 的模型、綁定使用者。
        var generated = factory.LiteLlm.GenerateRequests.Skip(issuedBefore).ToList();
        var request = Assert.Single(generated);
        Assert.Equal([FakeLlmEndpoints.ModelId], request["models"]!.AsArray().Select(m => m!.GetValue<string>()));
        Assert.Equal(me.Id.ToString("D"), request["metadata"]!["ymir_user_id"]!.GetValue<string>());

        // 模型呼叫全部使用 virtual key；master key 從未被 Agent 使用。
        Assert.NotEmpty(factory.LiteLlm.UsedApiKeys);
        Assert.All(factory.LiteLlm.UsedApiKeys, key =>
        {
            Assert.NotNull(key);
            Assert.StartsWith("sk-fake-", key, StringComparison.Ordinal);
            Assert.NotEqual(LiteLlmPiApiFactory.MasterKey, key);
        });
    }

    [Fact]
    public async Task DifferentUsers_GetDifferentVirtualKeys()
    {
        Assert.SkipUnless(PiHarnessFixture.IsPiOnPath(), "pi is not on PATH (npm i -g @earendil-works/pi-coding-agent@1.0.0)");
        using var alice = await factory.LoginAsync("litellm-alice");
        using var bob = await factory.LoginAsync("litellm-bob");

        foreach (var client in new[] { alice, bob })
        {
            var conversation = await client.CreateConversationAsync(null, "chat");
            var (_, sent) = await client.SendMessageAsync(conversation.Id, "hi");
            var events = await client.ReadEventsAsync(sent!.EventStreamUrl);
            Assert.Equal(ExecutionEventNames.ExecutionCompleted, events[^1].EventType);
        }

        var lastTwoKeys = factory.LiteLlm.UsedApiKeys.TakeLast(2).ToList();
        Assert.NotEqual(lastTwoKeys[0], lastTwoKeys[1]);
    }
}

public class UnreachableLiteLlmTests(UnreachableLiteLlmApiFactory factory) : IClassFixture<UnreachableLiteLlmApiFactory>
{
    [Fact]
    public async Task Execution_FailsWithModelProviderError_WhenKeyCannotBeIssued()
    {
        using var client = await factory.LoginAsync("litellm-down");
        var conversation = await client.CreateConversationAsync(null, "chat");

        var (_, sent) = await client.SendMessageAsync(conversation.Id, "hi");
        var events = await client.ReadEventsAsync(sent!.EventStreamUrl);

        var terminal = events[^1];
        Assert.Equal(ExecutionEventNames.ExecutionFailed, terminal.EventType);
        Assert.Equal(ExecutionErrorCodes.ModelProviderError, terminal.Data.GetProperty("code").GetString());
        Assert.DoesNotContain("127.0.0.1", terminal.Data.GetRawText(), StringComparison.Ordinal); // 不洩漏內部位址（SA §12）
    }
}
