using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ymir.IntegrationTests.Api;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Application.Executions;
using Ymir.VibeMaker.Application.Models;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Contracts.Conversations;
using Ymir.VibeMaker.Contracts.Executions;
using Ymir.VibeMaker.Contracts.Models;
using Ymir.VibeMaker.Domain;

namespace Ymir.IntegrationTests.Admin;

public sealed class ModelAccessApiFactory : ApiFactory
{
    protected override void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting("VibeMaker:Pi:ModelId", "one");
        builder.UseSetting("VibeMaker:Models:0:Id", "one"); builder.UseSetting("VibeMaker:Models:0:DisplayName", "One");
        builder.UseSetting("VibeMaker:Models:1:Id", "two"); builder.UseSetting("VibeMaker:Models:1:DisplayName", "Two");
        builder.UseSetting("VibeMaker:Models:1:SupportsThinking", "true");
        builder.UseSetting("VibeMaker:Models:1:Thinking:Parameter", "reasoning_effort");
        builder.UseSetting("VibeMaker:Models:1:Thinking:Levels:0", "low");
        builder.UseSetting("VibeMaker:Models:1:Thinking:Levels:1", "high");
        builder.UseSetting("VibeMaker:Models:1:Thinking:Levels:2", "xhigh");
        builder.UseSetting("VibeMaker:Models:1:Thinking:Levels:3", "max");
        builder.UseSetting("VibeMaker:Models:1:Thinking:DefaultLevel", "high");
        builder.UseSetting("VibeMaker:Models:1:Thinking:Required", "true");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IExecutionDispatcher>();
            services.AddSingleton<IExecutionDispatcher, HoldDispatcher>();
        });
    }

    private sealed class HoldDispatcher : IExecutionDispatcher
    {
        public ValueTask EnqueueAsync(Guid executionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken) => System.Threading.Channels.Channel.CreateUnbounded<Guid>().Reader.ReadAllAsync(cancellationToken);
    }
}

public class ModelAccessTests : IAsyncLifetime
{
    // 模型政策會寫入全域設定；每個測試用獨立資料庫，避免測試順序改變可選模型。
    private readonly ModelAccessApiFactory _factory = new();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ThinkingDepth_ValidatesCapabilityAndLevel_AndPersistsExecutionSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        using var member = await _factory.LoginAsync("thinking-member");
        var models = await member.GetFromJsonAsync<List<ModelResponse>>("/api/models", ct);
        Assert.True(models!.Single(m => m.Id == "two").SupportsThinking);
        Assert.Equal(["low", "high", "xhigh", "max"], models!.Single(m => m.Id == "two").Thinking!.Levels);
        Assert.False(models!.Single(m => m.Id == "one").SupportsThinking);
        var conversation = await member.CreateConversationAsync(null, "thinking depth");
        foreach (var (model, level) in new[] { ("one", "high"), ("two", "invalid"), ("two", "HIGH"), ("two", ""), ("two", "none"), ("two", "medium") })
        {
            using var rejected = await member.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages", new SendMessageRequest("hello", Guid.NewGuid(), model, ThinkingLevel: level), ct);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        var key = Guid.NewGuid();
        using var accepted = await member.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages", new SendMessageRequest("hello", key, "two", ThinkingLevel: "high"), ct);
        accepted.EnsureSuccessStatusCode();
        var queued = (await accepted.Content.ReadFromJsonAsync<SendMessageResponse>(ct))!;
        using var scope = _factory.Services.CreateScope();
        var execution = await scope.ServiceProvider.GetRequiredService<IVibeMakerDbContext>().AgentExecutions.SingleAsync(e => e.Id == queued.ExecutionId, ct);
        Assert.Equal("high", execution.ThinkingLevel);
        using var retry = await member.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages", new SendMessageRequest("hello", key, "two", ThinkingLevel: "low"), ct);
        Assert.Equal(queued.ExecutionId, (await retry.Content.ReadFromJsonAsync<SendMessageResponse>(ct))!.ExecutionId);
        await member.PostAsync($"/api/executions/{queued.ExecutionId}/cancel", null, ct);
    }

    [Fact]
    public async Task AdminPolicy_PersistsAndEnforcesApiSelection_AndResets()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await _factory.LoginAsync("model-admin", UserRole.Admin);
        using var member = await _factory.LoginAsync("model-member");
        using var forbidden = await member.PutAsJsonAsync("/api/admin/settings/models", new SaveModelAccessRequest(["two"], "two"), ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var invalid = await admin.PutAsJsonAsync("/api/admin/settings/models", new SaveModelAccessRequest([], "one"), ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var saved = await admin.PutAsJsonAsync("/api/admin/settings/models", new SaveModelAccessRequest(["two"], "two"), ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var state = await admin.GetFromJsonAsync<ModelAccessResponse>("/api/admin/settings/models", ct);
        Assert.False(state!.UsesDeployment);
        Assert.False(state.Models.Single(m => m.Id == "one").Enabled);
        Assert.Equal("two", state.DefaultModelId);
        var visible = await member.GetFromJsonAsync<List<ModelResponse>>("/api/models", ct);
        Assert.Equal("two", Assert.Single(visible!).Id);
        Assert.True(visible![0].IsDefault);
        // A fresh scope reads the saved setting (no singleton-only state).
        using var scope = _factory.Services.CreateScope();
        Assert.False((await scope.ServiceProvider.GetRequiredService<ModelAccessService>().GetAsync(ct)).IsAvailable("one"));
        var conversation = await member.CreateConversationAsync(null, "model policy");
        using var rejected = await member.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages", new SendMessageRequest("hello", Guid.NewGuid(), "one"), ct);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var reset = await admin.DeleteAsync("/api/admin/settings/models", ct);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(2, (await member.GetFromJsonAsync<List<ModelResponse>>("/api/models", ct))!.Count);
        using var queuedResponse = await member.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages", new SendMessageRequest("queued", Guid.NewGuid(), "one"), ct);
        queuedResponse.EnsureSuccessStatusCode();
        var queued = (await queuedResponse.Content.ReadFromJsonAsync<SendMessageResponse>(ct))!;
        using var disable = await admin.PutAsJsonAsync("/api/admin/settings/models", new SaveModelAccessRequest(["two"], "two"), ct);
        disable.EnsureSuccessStatusCode();
        using var runScope = _factory.Services.CreateScope();
        await runScope.ServiceProvider.GetRequiredService<ExecutionRunner>().RunAsync(queued.ExecutionId, ct);
        var execution = await runScope.ServiceProvider.GetRequiredService<IVibeMakerDbContext>().AgentExecutions.SingleAsync(e => e.Id == queued.ExecutionId, ct);
        Assert.Equal(ExecutionStatus.Failed, execution.Status);
        Assert.Equal(ExecutionErrorCodes.ModelNotAvailable, execution.ErrorCode);
    }
}
