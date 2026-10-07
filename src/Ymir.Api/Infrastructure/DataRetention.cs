using Microsoft.Extensions.Options;
using Ymir.Platform.Auditing;
using Ymir.VibeMaker.Application.Executions;

namespace Ymir.Api.Infrastructure;

/// <summary>
/// 資料保存期限（設定區段 <c>Ymir:Retention</c>，Sprint 5 強化）。0 表示永久保留。
/// 對話、訊息、專案與 workspace 檔案不在這裡：依資料保存原則永久保留，只能封存（開發規劃待確認事項）。
/// </summary>
public sealed class RetentionOptions
{
    public const string SectionName = "Ymir:Retention";

    /// <summary>稽核紀錄保留天數（預設 365）。</summary>
    public int AuditLogDays { get; set; } = 365;

    /// <summary>已結束 execution 的事件保留天數（預設 90；只影響 SSE 重播，對話內容在 messages）。</summary>
    public int ExecutionEventDays { get; set; } = 90;

    /// <summary>清理間隔（小時，預設 24）。</summary>
    public double IntervalHours { get; set; } = 24;

    /// <summary>啟動後第一次清理前的等待（分鐘，預設 5，避開啟動時的負載）。</summary>
    public double InitialDelayMinutes { get; set; } = 5;
}

/// <summary>
/// 定期刪除超過保存期限的稽核紀錄與 execution 事件。跨模組的工作（Platform 稽核 + Vibe Maker 事件）在 Api 層組合（ADR-0001）。
/// 每次清理本身也寫一筆稽核（system.retention.purge），保留「刪了多少」的紀錄。
/// </summary>
internal sealed partial class DataRetentionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RetentionOptions> options,
    TimeProvider timeProvider,
    ILogger<DataRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (settings.AuditLogDays <= 0 && settings.ExecutionEventDays <= 0)
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(Math.Max(0, settings.InitialDelayMinutes)), timeProvider, stoppingToken).ConfigureAwait(false);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(Math.Max(0.01, settings.IntervalHours)), timeProvider);
            do
            {
                await PurgeOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 關機
        }
    }

    /// <returns>刪除的（稽核, 事件）筆數。</returns>
    internal async Task<(int AuditLogs, int ExecutionEvents)> PurgeOnceAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var audits = settings.AuditLogDays > 0
                    ? await scope.ServiceProvider.GetRequiredService<IAuditLogMaintenance>().PurgeOlderThanAsync(now.AddDays(-settings.AuditLogDays), cancellationToken).ConfigureAwait(false)
                    : 0;
                var events = settings.ExecutionEventDays > 0
                    ? await scope.ServiceProvider.GetRequiredService<ExecutionEventRetention>().PurgeOlderThanAsync(now.AddDays(-settings.ExecutionEventDays), cancellationToken).ConfigureAwait(false)
                    : 0;
                if (audits > 0 || events > 0)
                {
                    await scope.ServiceProvider.GetRequiredService<IAuditLog>().WriteAsync(
                        new AuditEntry("system", "system.retention.purge", "retention", $"audit_log={audits};execution_events={events}", AuditResult.Success, now, null),
                        cancellationToken).ConfigureAwait(false);
                }

                LogPurged(logger, audits, events);
                return (audits, events);
            }
        }
#pragma warning disable CA1031 // 清理失敗（例如資料庫暫時無法使用）只記錄，下一輪再試，不讓服務停止。
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogPurgeFailed(logger, ex);
            return (0, 0);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Data retention purged {AuditLogs} audit log entries and {ExecutionEvents} execution events")]
    private static partial void LogPurged(ILogger logger, int auditLogs, int executionEvents);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Data retention purge failed; will retry next interval")]
    private static partial void LogPurgeFailed(ILogger logger, Exception exception);
}
