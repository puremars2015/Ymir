using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ymir.Api.Auth;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.Executions;
using Ymir.Platform.Auditing;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Application.Admin;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Contracts.Executions;
using Ymir.VibeMaker.Domain;

namespace Ymir.IntegrationTests.Admin;

/// <summary>
/// 部署預設：閒置 0.001 分鐘（約 60 毫秒）就可停止；背景檢查間隔拉長，測試自己呼叫 <see cref="RuntimeLifecycleService"/>，結果可預期。
/// </summary>
public sealed class RuntimeLifecycleApiFactory : ApiFactory
{
    protected override void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting("VibeMaker:Runtime:IdleTimeoutMinutes", "0.001");
        builder.UseSetting("VibeMaker:Runtime:IdleCheckIntervalSeconds", "3600");
        builder.ConfigureTestServices(services =>
        {
            // Local runtime 沒有「已停止」狀態：包一層模擬主機重開機後 container 停在 Stopped（ADR-0033）。
            var descriptor = services.Last(d => d.ServiceType == typeof(IAgentRuntimeManager));
            services.Remove(descriptor);
            services.AddSingleton(sp => new RebootableRuntimeManager((IAgentRuntimeManager)ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!)));
            services.AddSingleton<IAgentRuntimeManager>(sp => sp.GetRequiredService<RebootableRuntimeManager>());
        });
    }
}

/// <summary>測試替身：<see cref="SimulateReboot"/> 後該使用者的 container 狀態為 Stopped，直到下一次 EnsureRuntime。</summary>
public sealed class RebootableRuntimeManager(IAgentRuntimeManager inner) : IAgentRuntimeManager
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, bool> _stopped = new();

    public void SimulateReboot(Guid userId) => _stopped[userId] = true;

    public Task<RuntimeInfo> EnsureRuntimeAsync(Guid userId, RuntimeNetworkAccess? network, CancellationToken cancellationToken)
    {
        _stopped.TryRemove(userId, out _);
        return inner.EnsureRuntimeAsync(userId, network, cancellationToken);
    }

    public RestrictedNetworkSupport RestrictedNetwork => inner.RestrictedNetwork;

    public Task StartAsync(Guid runtimeId, CancellationToken cancellationToken) => inner.StartAsync(runtimeId, cancellationToken);

    public Task StopAsync(Guid runtimeId, CancellationToken cancellationToken) => inner.StopAsync(runtimeId, cancellationToken);

    public Task DeleteAsync(Guid runtimeId, CancellationToken cancellationToken) => inner.DeleteAsync(runtimeId, cancellationToken);

    public Task<RuntimeInfo> GetStatusAsync(Guid runtimeId, CancellationToken cancellationToken) => inner.GetStatusAsync(runtimeId, cancellationToken);

    public async Task<RuntimeStatus> GetStatusForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        _stopped.ContainsKey(userId) ? RuntimeStatus.Stopped : await inner.GetStatusForUserAsync(userId, cancellationToken);

    public Task<bool> StopForUserAsync(Guid userId, CancellationToken cancellationToken) => inner.StopForUserAsync(userId, cancellationToken);

    public Task<IRuntimeProcess> StartProcessAsync(Guid runtimeId, RuntimeProcessSpec spec, CancellationToken cancellationToken) =>
        inner.StartProcessAsync(runtimeId, spec, cancellationToken);
}

/// <summary>執行政策（ADR-0011）：管理介面設定、配額、閒置停止、啟動時對帳與自動啟動（ADR-0033）、用量。</summary>
public class RuntimeLifecycleTests(RuntimeLifecycleApiFactory factory) : IClassFixture<RuntimeLifecycleApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()!;

    private static async Task<Guid> UserIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/me", Ct)).GetProperty("id").GetGuid();

    /// <summary>改政策的測試在結束前一律還原（同一個 fixture 共用設定）。</summary>
    private static async Task<RuntimePolicyResponse> SavePolicyAsync(HttpClient admin, SaveRuntimePolicyRequest request)
    {
        using var response = await admin.PutAsJsonAsync("/api/admin/settings/runtime", request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RuntimePolicyResponse>(JsonDefaults.Options, Ct))!;
    }

    private async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private Task<AgentRuntimeRecord> RuntimeRecordAsync(Guid userId) =>
        InScopeAsync(sp => sp.GetRequiredService<IVibeMakerDbContext>().AgentRuntimes.AsNoTracking()
            .SingleAsync(r => r.UserId == userId && r.Status != RuntimeStatus.Deleted, Ct));

    [Fact]
    public async Task Policy_DefaultsToDeployment_SavesAndResets()
    {
        using var admin = await factory.LoginAsync($"policy-admin-{Guid.NewGuid():N}", UserRole.Admin);

        var initial = await admin.GetFromJsonAsync<RuntimePolicyResponse>("/api/admin/settings/runtime", JsonDefaults.Options, Ct);
        Assert.Equal(OidcSettingsSource.Deployment, initial!.Source);
        Assert.Equal(0.001, initial.Effective.IdleTimeoutMinutes, 6);
        Assert.Equal(30, initial.Effective.ExecutionTimeoutMinutes);

        try
        {
            var saved = await SavePolicyAsync(admin, new SaveRuntimePolicyRequest(45, 20, 3, 100));
            Assert.Equal(OidcSettingsSource.Database, saved.Source);
            Assert.Equal(new RuntimePolicyValues(45, 20, 3, 100, 0), saved.Effective);
            Assert.Equal(0.001, saved.Deployment.IdleTimeoutMinutes, 6);
            Assert.NotNull(saved.UpdatedByName);

            using var invalid = await admin.PutAsJsonAsync("/api/admin/settings/runtime", new SaveRuntimePolicyRequest(-1, 0, 0, -5), Ct);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(invalid));

            var audit = await admin.GetFromJsonAsync<AuditLogPageResponse>("/api/admin/audit?action=admin.settings.runtime", JsonDefaults.Options, Ct);
            Assert.Contains(audit!.Items, e => e.Action == "admin.settings.runtime.update");
        }
        finally
        {
            using var reset = await admin.DeleteAsync("/api/admin/settings/runtime", Ct);
            var restored = await reset.Content.ReadFromJsonAsync<RuntimePolicyResponse>(JsonDefaults.Options, Ct);
            Assert.Equal(OidcSettingsSource.Deployment, restored!.Source);
        }
    }

    [Fact]
    public async Task DailyLimit_RejectsExtraMessages_WithQuotaExceeded()
    {
        using var admin = await factory.LoginAsync($"quota-admin-{Guid.NewGuid():N}", UserRole.Admin);
        await SavePolicyAsync(admin, new SaveRuntimePolicyRequest(30, 30, 5, 1));
        try
        {
            using var client = await factory.LoginAsync($"quota-user-{Guid.NewGuid():N}");
            var conversation = await client.CreateConversationAsync(null, "quota");
            var (_, first) = await client.SendMessageAsync(conversation.Id, "first");
            await client.ReadEventsAsync(first!.EventStreamUrl);

            var (second, accepted) = await client.SendMessageAsync(conversation.Id, "second");

            Assert.Null(accepted);
            Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
            Assert.Equal(ExecutionErrorCodes.QuotaExceeded, await ProblemCodeAsync(second));
        }
        finally
        {
            await admin.DeleteAsync("/api/admin/settings/runtime", Ct);
        }
    }

    [Fact]
    public async Task IdleRuntime_IsStoppedAndAudited_AndRestartsOnNextMessage()
    {
        using var client = await factory.LoginAsync($"idle-user-{Guid.NewGuid():N}");
        var userId = await UserIdAsync(client);
        var conversation = await client.CreateConversationAsync(null, "idle");
        var (_, sent) = await client.SendMessageAsync(conversation.Id, "hello");
        await client.ReadEventsAsync(sent!.EventStreamUrl);
        Assert.Equal(RuntimeStatus.Running, (await RuntimeRecordAsync(userId)).Status);

        await Task.Delay(TimeSpan.FromMilliseconds(200), Ct); // 超過 0.001 分鐘的閒置時間
        var stopped = await InScopeAsync(sp => sp.GetRequiredService<RuntimeLifecycleService>().StopIdleAsync(Ct));

        Assert.True(stopped >= 1);
        Assert.Equal(RuntimeStatus.Stopped, (await RuntimeRecordAsync(userId)).Status);
        using var admin = await factory.LoginAsync($"idle-admin-{Guid.NewGuid():N}", UserRole.Admin);
        var audit = await admin.GetFromJsonAsync<AuditLogPageResponse>($"/api/admin/audit?action=runtime.idle_stop&userId={userId}", JsonDefaults.Options, Ct);
        Assert.Contains(audit!.Items, e => e.Action == "runtime.idle_stop" && e.ActorName == "system");
        // SA §12：建立 runtime 也寫稽核（AC-12）
        var created = await admin.GetFromJsonAsync<AuditLogPageResponse>($"/api/admin/audit?action=runtime.create&userId={userId}", JsonDefaults.Options, Ct);
        Assert.Contains(created!.Items, e => e.Action == "runtime.create" && e.TargetId == userId.ToString("D"));

        // 停止不刪除檔案；下一次送訊息自動啟動，閒置時間從執行結束重新計算
        var (_, again) = await client.SendMessageAsync(conversation.Id, "again");
        var events = await client.ReadEventsAsync(again!.EventStreamUrl);
        Assert.Equal(ExecutionEventNames.ExecutionCompleted, events[^1].EventType);
        var record = await RuntimeRecordAsync(userId);
        Assert.Equal(RuntimeStatus.Running, record.Status);
        Assert.True(record.LastActiveAt > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task IdleStop_SkipsUsersWithARunningExecution()
    {
        using var client = await factory.LoginAsync($"busy-user-{Guid.NewGuid():N}");
        var userId = await UserIdAsync(client);
        var conversation = await client.CreateConversationAsync(null, "busy");
        var (_, first) = await client.SendMessageAsync(conversation.Id, "warm up");
        await client.ReadEventsAsync(first!.EventStreamUrl);
        var (_, slow) = await client.SendMessageAsync(conversation.Id, "long task");
        await Task.Delay(TimeSpan.FromMilliseconds(300), Ct);

        await InScopeAsync(sp => sp.GetRequiredService<RuntimeLifecycleService>().StopIdleAsync(Ct));

        Assert.NotEqual(RuntimeStatus.Stopped, (await RuntimeRecordAsync(userId)).Status);
        await client.ReadEventsAsync(slow!.EventStreamUrl);
    }

    [Fact]
    public async Task Reconcile_UpdatesRecordsToTheActualRuntimeState()
    {
        using var client = await factory.LoginAsync($"reconcile-user-{Guid.NewGuid():N}");
        var userId = await UserIdAsync(client);
        var conversation = await client.CreateConversationAsync(null, "reconcile");
        var (_, sent) = await client.SendMessageAsync(conversation.Id, "hello");
        await client.ReadEventsAsync(sent!.EventStreamUrl);

        // 模擬 container 在 Ymir 不知道的情況下消失（例如主機重開機）
        await InScopeAsync(sp => sp.GetRequiredService<IAgentRuntimeManager>().StopForUserAsync(userId, Ct));
        var changed = await InScopeAsync(sp => sp.GetRequiredService<RuntimeLifecycleService>().ReconcileAsync(Ct));

        Assert.True(changed >= 1);
        Assert.Equal(RuntimeStatus.NotCreated, (await RuntimeRecordAsync(userId)).Status);
        using var admin = await factory.LoginAsync($"reconcile-admin-{Guid.NewGuid():N}", UserRole.Admin);
        var audit = await admin.GetFromJsonAsync<AuditLogPageResponse>($"/api/admin/audit?action=runtime.reconcile&userId={userId}", JsonDefaults.Options, Ct);
        Assert.Contains(audit!.Items, e => e.Action == "runtime.reconcile");
    }

    [Fact]
    public async Task Reconcile_StartsRuntimesStoppedByAHostRestart()
    {
        using var client = await factory.LoginAsync($"autostart-user-{Guid.NewGuid():N}");
        var userId = await UserIdAsync(client);
        var conversation = await client.CreateConversationAsync(null, "autostart");
        var (_, sent) = await client.SendMessageAsync(conversation.Id, "hello");
        await client.ReadEventsAsync(sent!.EventStreamUrl);
        var manager = factory.Services.GetRequiredService<RebootableRuntimeManager>();
        var lastActive = (await RuntimeRecordAsync(userId)).LastActiveAt;

        // 主機重開機：container 停在 Stopped，不是平台刻意停止 → API 啟動對帳時啟動回來
        manager.SimulateReboot(userId);
        await InScopeAsync(sp => sp.GetRequiredService<RuntimeLifecycleService>().ReconcileAsync(Ct));

        var record = await RuntimeRecordAsync(userId);
        Assert.Equal(RuntimeStatus.Running, record.Status);
        Assert.False(record.StopRequested);
        Assert.Equal(lastActive, record.LastActiveAt); // 自動啟動不是使用者活動
        Assert.Equal(RuntimeStatus.Running, await manager.GetStatusForUserAsync(userId, Ct));
        using var admin = await factory.LoginAsync($"autostart-admin-{Guid.NewGuid():N}", UserRole.Admin);
        var audit = await admin.GetFromJsonAsync<AuditLogPageResponse>($"/api/admin/audit?action=runtime.autostart&userId={userId}", JsonDefaults.Options, Ct);
        Assert.Contains(audit!.Items, e => e.Action == "runtime.autostart" && e.ActorName == "system");
    }

    [Fact]
    public async Task Reconcile_DoesNotStartRuntimesStoppedByAnAdmin_UntilTheNextExecution()
    {
        using var client = await factory.LoginAsync($"manual-stop-user-{Guid.NewGuid():N}");
        var userId = await UserIdAsync(client);
        var conversation = await client.CreateConversationAsync(null, "manual stop");
        var (_, sent) = await client.SendMessageAsync(conversation.Id, "hello");
        await client.ReadEventsAsync(sent!.EventStreamUrl);
        var manager = factory.Services.GetRequiredService<RebootableRuntimeManager>();

        using var admin = await factory.LoginAsync($"manual-stop-admin-{Guid.NewGuid():N}", UserRole.Admin);
        using var stop = await admin.PostAsync($"/api/admin/runtimes/{userId}/stop", null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, stop.StatusCode);
        Assert.True((await RuntimeRecordAsync(userId)).StopRequested);

        manager.SimulateReboot(userId);
        await InScopeAsync(sp => sp.GetRequiredService<RuntimeLifecycleService>().ReconcileAsync(Ct));
        var record = await RuntimeRecordAsync(userId);
        Assert.Equal(RuntimeStatus.Stopped, record.Status);
        Assert.True(record.StopRequested);

        // 使用者再次送訊息時照常啟動，人為停止的標記清除
        var (_, again) = await client.SendMessageAsync(conversation.Id, "again");
        await client.ReadEventsAsync(again!.EventStreamUrl);
        record = await RuntimeRecordAsync(userId);
        Assert.Equal(RuntimeStatus.Running, record.Status);
        Assert.False(record.StopRequested);
    }

    [Fact]
    public async Task Usage_ListsPerUserExecutions()
    {
        using var client = await factory.LoginAsync($"usage-user-{Guid.NewGuid():N}");
        var userId = await UserIdAsync(client);
        var conversation = await client.CreateConversationAsync(null, "usage");
        for (var i = 0; i < 2; i++)
        {
            var (_, sent) = await client.SendMessageAsync(conversation.Id, $"hello {i}");
            await client.ReadEventsAsync(sent!.EventStreamUrl);
        }

        using var admin = await factory.LoginAsync($"usage-admin-{Guid.NewGuid():N}", UserRole.Admin);
        var usage = await admin.GetFromJsonAsync<AdminUsageResponse>("/api/admin/usage?days=7", JsonDefaults.Options, Ct);

        var row = Assert.Single(usage!.Users, u => u.UserId == userId);
        Assert.Equal(7, usage.Days);
        Assert.False(usage.ModelUsageAvailable); // 沒有連接 LiteLLM
        Assert.Null(row.SpendUsd);
        Assert.Equal(2, row.Executions);
        Assert.Equal(2, row.Completed);
        Assert.Equal(2, row.Last24Hours);
        Assert.NotNull(row.LastExecutionAt);
        Assert.NotEmpty(row.DisplayName);

        // 依模型拆分（ADR-0021）：沒有連接 LiteLLM 時只有執行數，LiteLLM 欄位為 null
        var model = Assert.Single(row.Models);
        Assert.Equal("fake-model", model.ModelId);
        Assert.Equal(ModelUsageKind.Catalog, model.Kind);
        Assert.Equal(2, model.Executions);
        Assert.Null(model.SpendUsd);
        Assert.Null(model.ModelRequests);

        using var invalid = await admin.GetAsync("/api/admin/usage?days=365", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
}
