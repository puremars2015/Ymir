using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ymir.Platform.Auditing;
using Ymir.VibeMaker.Application.Executions;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Runtime;

/// <summary>
/// Runtime 生命週期（SA §15、ADR-0011）：
/// <list type="bullet">
/// <item>啟動時對帳：資料庫紀錄的狀態與實際 container 狀態不同時（例如主機重開機、手動停止），以實際狀態為準。</item>
/// <item>閒置停止：超過閒置時間沒有 execution 的 runtime 自動停止；檔案保留，下一次送訊息時自動啟動。</item>
/// </list>
/// 一律以 user id 操作 runtime（runtime id 在服務重新啟動後會改變，見 <see cref="IAgentRuntimeManager.StopForUserAsync"/>）。
/// </summary>
public sealed class RuntimeLifecycleService(
    IVibeMakerDbContext db,
    IAgentRuntimeManager runtimes,
    UserExecutionLocks userLocks,
    RuntimePolicyService policies,
    IAuditLog auditLog,
    TimeProvider timeProvider,
    ILogger<RuntimeLifecycleService> logger)
{
    private static readonly RuntimeStatus[] ActiveStatuses = [RuntimeStatus.Created, RuntimeStatus.Running, RuntimeStatus.Busy];

    /// <returns>狀態有變更的紀錄數。</returns>
    public async Task<int> ReconcileAsync(CancellationToken cancellationToken)
    {
        var records = await db.AgentRuntimes.Where(r => r.Status != RuntimeStatus.Deleted).ToListAsync(cancellationToken).ConfigureAwait(false);
        var changed = 0;
        foreach (var record in records)
        {
            try
            {
                var actual = await runtimes.GetStatusForUserAsync(record.UserId, cancellationToken).ConfigureAwait(false);
                if (actual != record.Status)
                {
                    logger.LogInformation("Runtime of user {UserId} reconciled from {Recorded} to {Actual}", record.UserId, record.Status, actual);
                    record.MarkStatus(actual, timeProvider.GetUtcNow());
                    changed++;
                }
            }
#pragma warning disable CA1031 // 單一使用者查詢失敗（例如 runtime host 暫時無法連線）不影響其他紀錄。
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                logger.LogWarning(ex, "Failed to reconcile runtime of user {UserId}", record.UserId);
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return changed;
    }

    /// <returns>這次停止的 runtime 數。</returns>
    public async Task<int> StopIdleAsync(CancellationToken cancellationToken)
    {
        var policy = await policies.GetAsync(cancellationToken).ConfigureAwait(false);
        if (policy.IdleTimeout <= TimeSpan.Zero)
        {
            return 0; // 不自動停止
        }

        var cutoff = timeProvider.GetUtcNow() - policy.IdleTimeout;
        var candidates = await db.AgentRuntimes
            .Where(r => ActiveStatuses.Contains(r.Status) && (r.LastActiveAt ?? r.UpdatedAt) <= cutoff)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var stopped = 0;
        foreach (var record in candidates)
        {
            // 使用者正在執行（持有 lock）時跳過；取得 lock 後 execution 會等待，停止完成後再由 EnsureRuntime 重新啟動。
            using var userLock = userLocks.TryAcquire(record.UserId);
            if (userLock is null)
            {
                continue;
            }

            var hasActiveExecution = await db.AgentExecutions.AnyAsync(
                e => e.UserId == record.UserId && (e.Status == ExecutionStatus.Queued || e.Status == ExecutionStatus.Running),
                cancellationToken).ConfigureAwait(false);
            if (hasActiveExecution)
            {
                continue;
            }

            try
            {
                await runtimes.StopForUserAsync(record.UserId, cancellationToken).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // 停止失敗只記錄，下一輪再試；不影響其他使用者。
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                logger.LogWarning(ex, "Failed to stop idle runtime of user {UserId}", record.UserId);
                continue;
            }

            var now = timeProvider.GetUtcNow();
            record.MarkStatus(RuntimeStatus.Stopped, now);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await auditLog.WriteAsync(
                new AuditEntry("system", "runtime.idle_stop", "user", record.UserId.ToString("D"), AuditResult.Success, now, null),
                cancellationToken).ConfigureAwait(false);
            stopped++;
        }

        if (stopped > 0)
        {
            logger.LogInformation("Stopped {Count} idle runtimes (idle timeout {IdleTimeout})", stopped, policy.IdleTimeout);
        }

        return stopped;
    }
}
