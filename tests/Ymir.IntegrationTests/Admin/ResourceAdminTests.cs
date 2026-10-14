using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Ymir.Api.Endpoints;
using Ymir.Api.Maintenance;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.Maintenance;
using Ymir.Maintenance;
using Ymir.MaintenanceHost;
using Ymir.Platform.Users;

namespace Ymir.IntegrationTests.Admin;

/// <summary>ADR-0025：開發環境沒有主機維護服務，管理介面顯示不支援，但政策仍可保存（寫稽核）。</summary>
public class ResourceAdminTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutMaintenanceHost_EverythingReportsUnsupported()
    {
        using var admin = await factory.LoginAsync($"res-dev-{Guid.NewGuid():N}", UserRole.Admin);

        var resources = await admin.GetFromJsonAsync<AdminResourcesResponse>("/api/admin/resources", JsonDefaults.Options, Ct);
        var runs = await admin.GetFromJsonAsync<AdminReclaimRunsResponse>("/api/admin/resources/runs", JsonDefaults.Options, Ct);
        using var dryRun = await admin.PostAsync("/api/admin/resources/dry-run", null, Ct);
        var dry = await dryRun.Content.ReadFromJsonAsync<AdminDryRunResponse>(JsonDefaults.Options, Ct);

        Assert.False(resources!.Supported);
        Assert.Null(resources.Snapshot);
        Assert.False(runs!.Supported);
        Assert.False(dry!.Supported);
    }

    [Fact]
    public async Task Policy_IsValidatedSavedAndAudited()
    {
        using var admin = await factory.LoginAsync($"res-policy-{Guid.NewGuid():N}", UserRole.Admin);

        var initial = await admin.GetFromJsonAsync<ReclamationPolicyResponse>("/api/admin/resources/policy", JsonDefaults.Options, Ct);
        Assert.False(initial!.Policy.Enabled); // 預設關閉自動回收

        using var invalid = await admin.PutAsJsonAsync("/api/admin/resources/policy", ReclamationPolicy.Default with { ImageRetentionDays = -3 }, JsonDefaults.Options, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var policy = ReclamationPolicy.Default with { Enabled = true, ContainerRetentionDays = 14, ImageRetentionDays = 30, DiskMinAvailableGb = 20, DiskMinAvailablePercent = 10 };
        using var saved = await admin.PutAsJsonAsync("/api/admin/resources/policy", policy, JsonDefaults.Options, Ct);
        var body = await saved.Content.ReadFromJsonAsync<ReclamationPolicyResponse>(JsonDefaults.Options, Ct);

        Assert.Equal(policy, body!.Policy);
        Assert.False(body.Supported);
        Assert.Null(body.Synced);
        Assert.NotNull(body.UpdatedAt);

        var audit = await admin.GetFromJsonAsync<AuditLogPageResponse>("/api/admin/audit?action=admin.settings.reclamation", JsonDefaults.Options, Ct);
        Assert.Contains(audit!.Items, i => i.Action == "admin.settings.reclamation.update");
    }
}

/// <summary>API 經 Unix socket 連到真正的維護服務（container CLI 為假的實作）。</summary>
public sealed class MaintenanceApiFactory : ApiFactory, IAsyncLifetime
{
    public MaintenanceHostFixture Host { get; } = new();

    public ValueTask InitializeAsync() => Host.InitializeAsync();

    protected override void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting("Ymir:Maintenance:Endpoint", Host.Endpoint);
        builder.UseSetting("Ymir:Maintenance:Token", MaintenanceHostFixture.Token);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await Host.DisposeAsync();
    }
}

public class ConnectedResourceAdminTests(MaintenanceApiFactory factory) : IClassFixture<MaintenanceApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Resources_PolicySync_AndRunAudit()
    {
        using var admin = await factory.LoginAsync($"res-host-{Guid.NewGuid():N}", UserRole.Admin);

        var resources = await admin.GetFromJsonAsync<AdminResourcesResponse>("/api/admin/resources", JsonDefaults.Options, Ct);
        Assert.True(resources!.Supported);
        Assert.True(resources.Available);
        Assert.Single(resources.Snapshot!.Containers);

        var policy = ReclamationPolicy.Default with { Enabled = true, ImageRetentionDays = 7 };
        using var saved = await admin.PutAsJsonAsync("/api/admin/resources/policy", policy, JsonDefaults.Options, Ct);
        var body = await saved.Content.ReadFromJsonAsync<ReclamationPolicyResponse>(JsonDefaults.Options, Ct);
        Assert.True(body!.Synced);
        Assert.Equal(policy, factory.Host.Services.GetRequiredService<MaintenanceService>().Policy);

        // 維護服務留下一筆有刪除的回收紀錄 → 同步工作寫入稽核（摘要，不含項目細節）。
        factory.Host.Services.GetRequiredService<MaintenanceStateStore>().AddRun(id => new ReclaimRun(
            id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, ReclaimTrigger.Retention, 1, 0, 1, 0, [], [new ReclaimItem("ymir", "image", "x", ReclaimItemResult.Deleted, null)], "依保留天數：刪除 1"));
        var worker = factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<MaintenanceSyncWorker>().Single();
        await worker.SyncAsync(Ct);

        var audit = await admin.GetFromJsonAsync<AuditLogPageResponse>("/api/admin/audit?action=maintenance.reclaim", JsonDefaults.Options, Ct);
        Assert.Contains(audit!.Items, i => i.Action == "maintenance.reclaim" && i.TargetType == "maintenance-run");

        var runs = await admin.GetFromJsonAsync<AdminReclaimRunsResponse>("/api/admin/resources/runs", JsonDefaults.Options, Ct);
        Assert.True(runs!.Available);
        Assert.Contains(runs.Runs, r => r.Deleted == 1);
    }

    [Fact]
    public async Task NonAdmin_IsForbidden()
    {
        using var user = await factory.LoginAsync($"res-user-{Guid.NewGuid():N}");
        using var response = await user.GetAsync("/api/admin/resources", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
