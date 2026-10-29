using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.Executions;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Domain;

namespace Ymir.IntegrationTests.Admin;

/// <summary>
/// Local runtime 沒有 container（背景 key 會略過）：包一層假裝是 container，並攔下寫 key 的程序、記錄它從 stdin 收到的內容。
/// 其他程序（Scripted harness 等）照常交給 Local runtime。
/// </summary>
public sealed class BackgroundCredentialApiFactory : ApiFactory
{
    protected override void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting("VibeMaker:Runtime:IdleCheckIntervalSeconds", "3600");
        builder.ConfigureTestServices(services =>
        {
            var descriptor = services.Last(d => d.ServiceType == typeof(IAgentRuntimeManager));
            services.Remove(descriptor);
            services.AddSingleton(sp => new KeyCapturingRuntimeManager((IAgentRuntimeManager)ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!)));
            services.AddSingleton<IAgentRuntimeManager>(sp => sp.GetRequiredService<KeyCapturingRuntimeManager>());
        });
    }
}

public sealed class KeyCapturingRuntimeManager(IAgentRuntimeManager inner) : IAgentRuntimeManager
{
    private readonly ConcurrentDictionary<Guid, Guid> _usersByRuntime = new();

    /// <summary>每位使用者最後一次寫入的內容（空字串 = 刪除 key）與程序規格。</summary>
    public ConcurrentDictionary<Guid, (string Stdin, RuntimeProcessSpec Spec)> Writes { get; } = new();

    public async Task<RuntimeInfo> EnsureRuntimeAsync(Guid userId, RuntimeNetworkAccess? network, CancellationToken cancellationToken)
    {
        var info = await inner.EnsureRuntimeAsync(userId, network, cancellationToken);
        _usersByRuntime[info.RuntimeId] = userId;
        return info with { Provider = "PODMAN" };
    }

    public RestrictedNetworkSupport RestrictedNetwork => inner.RestrictedNetwork;

    public Task StartAsync(Guid runtimeId, CancellationToken cancellationToken) => inner.StartAsync(runtimeId, cancellationToken);

    public Task StopAsync(Guid runtimeId, CancellationToken cancellationToken) => inner.StopAsync(runtimeId, cancellationToken);

    public Task DeleteAsync(Guid runtimeId, CancellationToken cancellationToken) => inner.DeleteAsync(runtimeId, cancellationToken);

    public Task<RuntimeInfo> GetStatusAsync(Guid runtimeId, CancellationToken cancellationToken) => inner.GetStatusAsync(runtimeId, cancellationToken);

    public Task<RuntimeStatus> GetStatusForUserAsync(Guid userId, CancellationToken cancellationToken) => inner.GetStatusForUserAsync(userId, cancellationToken);

    public Task<bool> StopForUserAsync(Guid userId, CancellationToken cancellationToken) => inner.StopForUserAsync(userId, cancellationToken);

    public Task<IRuntimeProcess> StartProcessAsync(Guid runtimeId, RuntimeProcessSpec spec, CancellationToken cancellationToken)
    {
        if (spec.Arguments.Contains(BackgroundWork.WriteKeyScript))
        {
            return Task.FromResult<IRuntimeProcess>(new CapturingProcess(stdin => Writes[_usersByRuntime[runtimeId]] = (stdin, spec)));
        }

        return inner.StartProcessAsync(runtimeId, spec, cancellationToken);
    }

    private sealed class CapturingProcess(Action<string> onClosed) : IRuntimeProcess
    {
        private readonly MemoryStream _stdin = new();

        public Stream StandardInput => _stdin;

        public Stream StandardOutput { get; } = new MemoryStream();

        public bool HasExited { get; private set; }

        public void CloseStandardInput()
        {
            onClosed(Encoding.UTF8.GetString(_stdin.ToArray()));
            HasExited = true;
        }

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public void Kill() => HasExited = true;

        public string GetStandardErrorTail() => string.Empty;

        public ValueTask DisposeAsync()
        {
            _stdin.Dispose();
            StandardOutput.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>ADR-0034：背景程序的模型 key 只給 Active 使用者、執行中且不是人為停止的 runtime，經 stdin 寫入。</summary>
public class BackgroundCredentialTests(BackgroundCredentialApiFactory factory) : IClassFixture<BackgroundCredentialApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private KeyCapturingRuntimeManager Manager => factory.Services.GetRequiredService<KeyCapturingRuntimeManager>();

    private async Task<(HttpClient Client, Guid UserId)> UserWithRuntimeAsync(string prefix)
    {
        var client = await factory.LoginAsync($"{prefix}-{Guid.NewGuid():N}");
        var userId = (await client.GetFromJsonAsync<JsonElement>("/api/me", Ct)).GetProperty("id").GetGuid();
        var conversation = await client.CreateConversationAsync(null, prefix);
        var (_, sent) = await client.SendMessageAsync(conversation.Id, "hello");
        await client.ReadEventsAsync(sent!.EventStreamUrl);
        return (client, userId);
    }

    private async Task SyncAllAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<BackgroundCredentialService>().SyncAllAsync(Ct);
    }

    [Fact]
    public async Task NewRuntime_GetsTheKeyThroughStdin_NotTheArguments()
    {
        var (client, userId) = await UserWithRuntimeAsync("bg-key");
        using (client)
        {
            // 第一次執行建立 runtime：ExecutionRunner 立即寫入
            Assert.True(Manager.Writes.TryGetValue(userId, out var write));
            Assert.False(string.IsNullOrEmpty(write.Stdin));
            Assert.Equal(RuntimePaths.Workspace, write.Spec.WorkingDirectory);
            Assert.Null(write.Spec.Environment);
            Assert.DoesNotContain(write.Spec.Arguments, a => a.Contains(write.Stdin, StringComparison.Ordinal));

            // 背景 worker 每一輪重寫（container 重新啟動後 tmpfs 會清空）
            Manager.Writes.TryRemove(userId, out _);
            await SyncAllAsync();
            Assert.True(Manager.Writes.TryGetValue(userId, out var again));
            Assert.False(string.IsNullOrEmpty(again.Stdin));
        }
    }

    [Fact]
    public async Task StoppedDisabledAndGroupRuntimes_GetNoKey()
    {
        var (stoppedClient, stoppedId) = await UserWithRuntimeAsync("bg-stopped");
        var (disabledClient, disabledId) = await UserWithRuntimeAsync("bg-disabled");
        stoppedClient.Dispose();
        disabledClient.Dispose();
        var groupId = Guid.NewGuid();

        using (var admin = await factory.LoginAsync($"bg-admin-{Guid.NewGuid():N}", UserRole.Admin))
        using (var stop = await admin.PostAsync($"/api/admin/runtimes/{stoppedId}/stop", null, Ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, stop.StatusCode);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IUserDirectory>().SetStatusAsync(disabledId, UserStatus.Disabled, Ct);
            // 群組 runtime（ADR-0026）：擁有者 id 不是使用者
            var db = scope.ServiceProvider.GetRequiredService<IVibeMakerDbContext>();
            db.AgentRuntimes.Add(AgentRuntimeRecord.Create(Guid.NewGuid(), groupId, "PODMAN", "group", "image", RuntimeStatus.Running, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(Ct);
        }

        Manager.Writes.Clear();
        await SyncAllAsync();

        Assert.False(Manager.Writes.ContainsKey(stoppedId));
        Assert.False(Manager.Writes.ContainsKey(disabledId));
        Assert.False(Manager.Writes.ContainsKey(groupId));
    }

    [Fact]
    public async Task DisablingBackgroundModelAccess_RemovesTheKey()
    {
        var (client, userId) = await UserWithRuntimeAsync("bg-policy");
        client.Dispose();
        using var admin = await factory.LoginAsync($"bg-policy-admin-{Guid.NewGuid():N}", UserRole.Admin);
        using (var save = await admin.PutAsJsonAsync("/api/admin/settings/runtime", new SaveRuntimePolicyRequest(0, 30, 5, 0, BackgroundModelAccess: false), Ct))
        {
            Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        }

        try
        {
            await SyncAllAsync();
            Assert.True(Manager.Writes.TryGetValue(userId, out var write));
            Assert.Equal(string.Empty, write.Stdin); // 空的 stdin：寫入腳本刪除 key 檔
        }
        finally
        {
            await admin.DeleteAsync("/api/admin/settings/runtime", Ct);
        }
    }
}
