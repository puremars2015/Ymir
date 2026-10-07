using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

public sealed class UserModelAccessTests : IAsyncLifetime
{
    private readonly ModelAccessApiFactory _factory = new();
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;
    public async ValueTask DisposeAsync() { await _factory.DisposeAsync().ConfigureAwait(false); GC.SuppressFinalize(this); }

    [Fact]
    public async Task UserOverrides_Persist_GrantSystemDisabledModel_DenyAllowedModel_AndInheritChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await _factory.LoginAsync("admin", UserRole.Admin);
        using var alice = await _factory.LoginAsync("alice");
        using var bob = await _factory.LoginAsync("bob");
        var userId = (await alice.GetFromJsonAsync<JsonElement>("/api/me", ct)).GetProperty("id").GetGuid();
        var url = $"/api/admin/users/{userId}/models";
        using var system = await admin.PutAsJsonAsync("/api/admin/settings/models", new SaveModelAccessRequest(["two"], "two"), ct);
        system.EnsureSuccessStatusCode();
        var inherited = (await admin.GetFromJsonAsync<UserModelAccessResponse>(url, ct))!;
        Assert.All(inherited.Models, m => Assert.Null(m.Override));
        Assert.False(inherited.Models.Single(m => m.Id == "one").SystemEnabled);
        using var saved = await admin.PutAsJsonAsync(url, new SaveUserModelAccessRequest(new Dictionary<string, bool> { ["one"] = true, ["two"] = false }), ct);
        saved.EnsureSuccessStatusCode();
        Assert.Equal("one", Assert.Single((await alice.GetFromJsonAsync<List<ModelResponse>>("/api/models", ct))!).Id);
        Assert.Equal("two", Assert.Single((await bob.GetFromJsonAsync<List<ModelResponse>>("/api/models", ct))!).Id);
        using var scope = _factory.Services.CreateScope();
        Assert.Equal("one", (await scope.ServiceProvider.GetRequiredService<ModelAccessService>().GetForUserAsync(userId, ct)).DefaultModelId);
        var conversation = await alice.CreateConversationAsync(null, "model access");
        using var denied = await alice.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages", new SendMessageRequest("denied", Guid.NewGuid(), "two"), ct);
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        using var allowed = await alice.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages", new SendMessageRequest("allowed", Guid.NewGuid(), "one"), ct);
        allowed.EnsureSuccessStatusCode();
        var queued = (await allowed.Content.ReadFromJsonAsync<SendMessageResponse>(ct))!;
        // 排隊執行啟動時再讀個人權限，不沿用送出時的可用清單。
        using var revoke = await admin.PutAsJsonAsync(url, new SaveUserModelAccessRequest(new Dictionary<string, bool> { ["one"] = false, ["two"] = false }), ct);
        revoke.EnsureSuccessStatusCode();
        using var runScope = _factory.Services.CreateScope();
        await runScope.ServiceProvider.GetRequiredService<ExecutionRunner>().RunAsync(queued.ExecutionId, ct);
        var execution = await runScope.ServiceProvider.GetRequiredService<IVibeMakerDbContext>().AgentExecutions.SingleAsync(e => e.Id == queued.ExecutionId, ct);
        Assert.Equal(ExecutionErrorCodes.ModelNotAvailable, execution.ErrorCode);
        Assert.Empty((await alice.GetFromJsonAsync<List<ModelResponse>>("/api/models", ct))!);
        using var noModels = await alice.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages", new SendMessageRequest("none", Guid.NewGuid()), ct);
        Assert.Equal(HttpStatusCode.BadRequest, noModels.StatusCode);
        using var reset = await admin.DeleteAsync(url, ct);
        reset.EnsureSuccessStatusCode();
        using var changed = await admin.DeleteAsync("/api/admin/settings/models", ct);
        changed.EnsureSuccessStatusCode();
        Assert.Equal(2, (await alice.GetFromJsonAsync<List<ModelResponse>>("/api/models", ct))!.Count);
        using var audit = await admin.GetAsync($"/api/admin/audit?action=admin.user.models&targetId={userId}", ct);
        audit.EnsureSuccessStatusCode();
        Assert.Contains("admin.user.models.update", await audit.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        Assert.Contains("admin.user.models.reset", await audit.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidOrUnknownUser_AndMemberWrites_AreRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await _factory.LoginAsync("admin", UserRole.Admin);
        using var member = await _factory.LoginAsync("member");
        var id = (await member.GetFromJsonAsync<JsonElement>("/api/me", ct)).GetProperty("id").GetGuid();
        var request = new SaveUserModelAccessRequest(new Dictionary<string, bool> { ["unknown"] = true });
        using var invalid = await admin.PutAsJsonAsync($"/api/admin/users/{id}/models", request, ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Put, HttpMethod.Delete })
        {
            using var message = new HttpRequestMessage(method, $"/api/admin/users/{Guid.NewGuid()}/models");
            if (method == HttpMethod.Put) message.Content = JsonContent.Create(new SaveUserModelAccessRequest(new Dictionary<string, bool>()));
            using var notFound = await admin.SendAsync(message, ct);
            Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        }
        using var forbidden = await member.PutAsJsonAsync($"/api/admin/users/{id}/models", request, ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}
