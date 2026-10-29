using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ymir.Platform.Auditing;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Application.Executions;
using Ymir.VibeMaker.Application.Extensions;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Runtime;

/// <summary>
/// Runtime 生命週期（SA §15、ADR-0011）：
/// <list type="bullet">
/// <item>啟動時對帳：資料庫紀錄的狀態與實際 container 狀態不同時（例如主機重開機、手動停止），以實際狀態為準。</item>
/// <item>常駐（ADR-0033）：對帳後把「不是平台刻意停止」的已停止 runtime 啟動回來（例如主機重開機），
/// 讓使用者在 container 內的長任務與排程可以持續；非 Active 使用者的 runtime 則停止。</item>
/// <item>閒置停止：預設關閉（0）；管理員開啟後，超過閒置時間沒有 execution 的 runtime 自動停止；檔案保留，下一次送訊息時自動啟動。</item>
/// </list>
/// 一律以 user id 操作 runtime（runtime id 在服務重新啟動後會改變，見 <see cref="IAgentRuntimeManager.StopForUserAsync"/>）。
/// </summary>
public sealed class RuntimeLifecycleService(
    IVibeMakerDbContext db,
    IAgentRuntimeManager runtimes,
    UserExecutionLocks userLocks,
    RuntimePolicyService policies,
    ExtensionPolicyService extensionPolicy,
    IUserDirectory users,
    VibeMakerTelemetry telemetry,
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
                    await auditLog.WriteAsync(
                        new AuditEntry("system", "runtime.reconcile", "user", record.UserId.ToString("D"), AuditResult.Success, timeProvider.GetUtcNow(), null),
                        cancellationToken).ConfigureAwait(false);
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
        changed += await ApplyDesiredStateAsync(records, cancellationToken).ConfigureAwait(false);
        await UpdateActiveRuntimesAsync(cancellationToken).ConfigureAwait(false);
        return changed;
    }

    /// <summary>
    /// 對帳後讓 runtime 回到應有的狀態（ADR-0033）：
    /// <list type="bullet">
    /// <item>Active 使用者、已停止、而且不是平台刻意停止（<see cref="AgentRuntimeRecord.StopRequested"/>）→ 啟動。</item>
    /// <item>使用者已停用或刪除、runtime 仍在執行 → 停止（停用當下停止失敗時的補救）。</item>
    /// </list>
    /// 群組 runtime（id 不是使用者，ADR-0026）不處理，下一次 <c>@bot</c> 時照舊啟動。
    /// </summary>
    private async Task<int> ApplyDesiredStateAsync(List<AgentRuntimeRecord> records, CancellationToken cancellationToken)
    {
        var candidates = records
            .Where(r => (r.Status == RuntimeStatus.Stopped && !r.StopRequested) || ActiveStatuses.Contains(r.Status))
            .ToList();
        if (candidates.Count == 0)
        {
            return 0;
        }

        var owners = (await users.FindManyAsync([.. candidates.Select(r => r.UserId)], cancellationToken).ConfigureAwait(false))
            .ToDictionary(u => u.Id);
        var changed = 0;
        foreach (var record in candidates)
        {
            if (!owners.TryGetValue(record.UserId, out var owner))
            {
                continue;
            }

            // 使用者正在執行（持有 lock）時跳過：execution 自己會 EnsureRuntime。
            using var userLock = userLocks.TryAcquire(record.UserId);
            if (userLock is null)
            {
                continue;
            }

            try
            {
                if (owner.Status != UserStatus.Active)
                {
                    if (record.Status != RuntimeStatus.Stopped)
                    {
                        await runtimes.StopForUserAsync(record.UserId, cancellationToken).ConfigureAwait(false);
                        record.MarkStopped(timeProvider.GetUtcNow());
                        await SaveAndAuditAsync("runtime.stop", record.UserId, cancellationToken).ConfigureAwait(false);
                        changed++;
                    }

                    continue;
                }

                if (record.Status != RuntimeStatus.Stopped)
                {
                    continue;
                }

                // 網路模式與 ExecutionRunner 相同，由伺服器依擴充能力決定（ADR-0012 A.8）；
                // 受限網路未設定時 EnsureRuntime 會失敗，不得退回成可以對外連線。
                var extensions = await extensionPolicy.ResolveAsync(record.UserId, cancellationToken).ConfigureAwait(false);
                var runtime = await runtimes.EnsureRuntimeAsync(record.UserId, extensions.NetworkAccess, cancellationToken).ConfigureAwait(false);
                // 不是使用者活動：只更新狀態，LastActiveAt 不變。
                record.MarkStatus(runtime.Status, timeProvider.GetUtcNow());
                await SaveAndAuditAsync("runtime.autostart", record.UserId, cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Runtime of user {UserId} started after reconcile", record.UserId);
                changed++;
            }
#pragma warning disable CA1031 // 單一使用者啟動 / 停止失敗只記錄，不影響其他使用者；下一次 API 啟動再試。
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                logger.LogWarning(ex, "Failed to restore runtime state of user {UserId}", record.UserId);
            }
        }

        return changed;
    }

    private async Task SaveAndAuditAsync(string action, Guid userId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditLog.WriteAsync(new AuditEntry("system", action, "user", userId.ToString("D"), AuditResult.Success, now, null), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>SA §18 的 active runtimes 指標：背景每輪以資料庫紀錄更新。</summary>
    public async Task UpdateActiveRuntimesAsync(CancellationToken cancellationToken) =>
        telemetry.SetActiveRuntimes(await db.AgentRuntimes.CountAsync(r => ActiveStatuses.Contains(r.Status), cancellationToken).ConfigureAwait(false));

    /// <returns>這次停止的 runtime 數。</returns>
    public async Task<int> StopIdleAsync(CancellationToken cancellationToken)
    {
        var policy = await policies.GetAsync(cancellationToken).ConfigureAwait(false);
        if (policy.IdleTimeout <= TimeSpan.Zero)
        {
            await UpdateActiveRuntimesAsync(cancellationToken).ConfigureAwait(false);
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
                // 群組 Agent（ADR-0026）的 runtime 擁有者是群組，execution 的 user 是呼叫者：以對話擁有者一併判斷。
                e => (e.UserId == record.UserId || db.Conversations.Any(c => c.Id == e.ConversationId && c.UserId == record.UserId))
                    && (e.Status == ExecutionStatus.Queued || e.Status == ExecutionStatus.Running),
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
            // 閒置停止是管理員開啟的政策：API 重新啟動時不自動啟動，下一次送訊息時才啟動。
            record.MarkStopped(now);
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

        await UpdateActiveRuntimesAsync(cancellationToken).ConfigureAwait(false);
        return stopped;
    }
}
