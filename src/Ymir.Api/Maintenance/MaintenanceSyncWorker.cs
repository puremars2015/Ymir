using System.Globalization;
using Ymir.Maintenance;
using Ymir.Platform.Auditing;
using Ymir.Platform.Settings;

namespace Ymir.Api.Maintenance;

/// <summary>
/// 每分鐘把保存的回收政策同步到主機維護服務，並把新的回收紀錄寫入稽核（ADR-0025 §1：保存與回收結果寫稽核）。
/// 只有實際刪除、失敗或由資源門檻觸發的回合寫稽核；依保留天數檢查但沒有任何動作的回合只留在服務的回收紀錄。
/// 沒有設定維護服務時不執行。
/// </summary>
internal sealed partial class MaintenanceSyncWorker(
    IMaintenanceClient client,
    IServiceScopeFactory scopeFactory,
    ILogger<MaintenanceSyncWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!client.IsConfigured)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await SyncAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // 同步失敗下一分鐘重試；不可讓背景工作停止。
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogSyncFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    internal async Task SyncAsync(CancellationToken cancellationToken)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var settings = scope.ServiceProvider.GetRequiredService<ISystemSettingsStore>();
            var policy = await ReclamationPolicyStore.GetAsync(settings, cancellationToken).ConfigureAwait(false);
            await client.SetPolicyAsync(policy.Policy, cancellationToken).ConfigureAwait(false);

            var lastImported = long.TryParse((await settings.GetAsync(ReclamationPolicyStore.LastImportedRunKey, cancellationToken).ConfigureAwait(false))?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var last) ? last : 0;
            var runs = await client.GetRunsAsync(lastImported, cancellationToken).ConfigureAwait(false);
            if (runs is null)
            {
                return;
            }

            if (runs.Count == 0 && lastImported > 0)
            {
                // 服務的紀錄被重設（id 從 1 重新開始）時，從最新一頁重新匯入。
                var latest = await client.GetRunsAsync(0, cancellationToken).ConfigureAwait(false);
                if (latest is { Count: > 0 } && latest[^1].Id < lastImported)
                {
                    runs = latest;
                }
            }

            if (runs.Count == 0)
            {
                return;
            }

            var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
            foreach (var run in runs.Where(ShouldAudit))
            {
                await audit.WriteAsync(
                    new AuditEntry(
                        "system",
                        "maintenance.reclaim",
                        "maintenance-run",
                        string.Create(CultureInfo.InvariantCulture, $"{run.Id};trigger={run.Trigger};deleted={run.Deleted};failed={run.Failed}"),
                        run.Failed > 0 ? AuditResult.Failure : AuditResult.Success,
                        run.FinishedAt,
                        null),
                    cancellationToken).ConfigureAwait(false);
            }

            await settings.SetAsync(ReclamationPolicyStore.LastImportedRunKey, runs.Max(r => r.Id).ToString(CultureInfo.InvariantCulture), "system", cancellationToken).ConfigureAwait(false);
        }
    }

    internal static bool ShouldAudit(ReclaimRun run) => run.Deleted > 0 || run.Failed > 0 || run.Trigger != ReclaimTrigger.Retention;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Maintenance policy / run sync failed")]
    private static partial void LogSyncFailed(ILogger logger, Exception exception);
}
