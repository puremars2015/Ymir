using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Auditing;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Admin;

/// <summary>
/// Admin 總覽的 Vibe Maker 統計（ADR-0010）：執行狀況、近幾天的執行數、各使用者的 runtime。
/// 「今天」依呼叫端傳入的 UTC 位移切日，伺服器不依賴時區資料庫。
/// </summary>
public sealed class AdminStatsService(
    IVibeMakerDbContext db,
    IAgentRuntimeManager runtimes,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public const int TrendDays = 7;

    public async Task<ExecutionStatistics> GetExecutionStatisticsAsync(TimeSpan utcOffset, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var localToday = now.ToOffset(utcOffset).Date;
        var trendStart = new DateTimeOffset(localToday.AddDays(-(TrendDays - 1)), utcOffset);
        var todayStart = new DateTimeOffset(localToday, utcOffset);

        var running = await db.AgentExecutions.AsNoTracking().CountAsync(e => e.Status == ExecutionStatus.Running, cancellationToken).ConfigureAwait(false);
        var queued = await db.AgentExecutions.AsNoTracking().CountAsync(e => e.Status == ExecutionStatus.Queued, cancellationToken).ConfigureAwait(false);

        var recent = await db.AgentExecutions.AsNoTracking()
            .Where(e => e.CreatedAt >= trendStart)
            .Select(e => new { e.CreatedAt, e.Status })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var today = recent.Where(e => e.CreatedAt >= todayStart).ToList();
        var trend = Enumerable.Range(0, TrendDays)
            .Select(i =>
            {
                var day = localToday.AddDays(i - (TrendDays - 1));
                var dayRows = recent.Where(e => e.CreatedAt.ToOffset(utcOffset).Date == day).ToList();
                return new DailyExecutionCount(
                    DateOnly.FromDateTime(day),
                    dayRows.Count,
                    dayRows.Count(e => e.Status == ExecutionStatus.Failed));
            })
            .ToList();

        return new ExecutionStatistics(
            running,
            queued,
            today.Count(e => e.Status == ExecutionStatus.Completed),
            today.Count(e => e.Status == ExecutionStatus.Failed),
            today.Count(e => e.Status == ExecutionStatus.Cancelled),
            trend);
    }

    /// <summary>未刪除的 runtime，依最後活動時間排序（狀態以資料庫紀錄為準，可能比實際 container 落後）。</summary>
    public async Task<IReadOnlyList<RuntimeSummary>> ListRuntimesAsync(CancellationToken cancellationToken) =>
        await db.AgentRuntimes.AsNoTracking()
            .Where(r => r.Status != RuntimeStatus.Deleted)
            .OrderByDescending(r => r.LastActiveAt ?? r.UpdatedAt)
            .Select(r => new RuntimeSummary(r.Id, r.UserId, r.Status, r.ImageVersion, r.LastActiveAt))
            .Take(500)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// 各使用者近 <paramref name="days"/> 天的用量：執行數、結果、Agent 實際執行時間，以及過去 24 小時的次數（與每日上限比較）。
    /// 依執行數由多到少排序。
    /// </summary>
    public async Task<IReadOnlyList<UserUsage>> GetUsageAsync(int days, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var since = now.AddDays(-days);
        var last24Hours = now.AddDays(-1);
        var rows = await db.AgentExecutions.AsNoTracking()
            .Where(e => e.CreatedAt >= since)
            .Select(e => new { e.UserId, e.Status, e.CreatedAt, e.StartedAt, e.EndedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var runtimeStatuses = await db.AgentRuntimes.AsNoTracking()
            .Where(r => r.Status != RuntimeStatus.Deleted)
            .Select(r => new { r.UserId, r.Status })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var statusByUser = runtimeStatuses.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => g.First().Status);

        return [.. rows.GroupBy(e => e.UserId)
            .Select(g => new UserUsage(
                g.Key,
                g.Count(),
                g.Count(e => e.Status == ExecutionStatus.Completed),
                g.Count(e => e.Status == ExecutionStatus.Failed),
                g.Count(e => e.Status == ExecutionStatus.Cancelled),
                TimeSpan.FromTicks(g.Where(e => e.StartedAt is not null && e.EndedAt is not null).Sum(e => (e.EndedAt!.Value - e.StartedAt!.Value).Ticks)),
                g.Count(e => e.CreatedAt > last24Hours),
                g.Max(e => e.CreatedAt),
                statusByUser.TryGetValue(g.Key, out var status) ? status : null))
            .OrderByDescending(u => u.Executions)
            .ThenByDescending(u => u.LastExecutionAt)];
    }

    /// <summary>用過 runtime 的使用者（模型用量要查的對象）。</summary>
    public async Task<IReadOnlyList<Guid>> ListRuntimeUserIdsAsync(CancellationToken cancellationToken) =>
        await db.AgentRuntimes.AsNoTracking().Select(r => r.UserId).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>停止指定使用者的 runtime（不刪除檔案）；沒有 runtime 時回傳 false。</summary>
    public async Task<bool> StopRuntimeAsync(Guid userId, string actor, CancellationToken cancellationToken)
    {
        var record = await db.AgentRuntimes
            .FirstOrDefaultAsync(r => r.UserId == userId && r.Status != RuntimeStatus.Deleted, cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return false;
        }

        // 以 user id 停止：runtime id 在服務重新啟動後會改變（見 IAgentRuntimeManager.StopForUserAsync）。
        await runtimes.StopForUserAsync(userId, cancellationToken).ConfigureAwait(false);
        record.MarkStatus(RuntimeStatus.Stopped, timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditLog.WriteAsync(
            new AuditEntry(actor, "admin.runtime.stop", "user", userId.ToString("D"), AuditResult.Success, timeProvider.GetUtcNow(), null),
            cancellationToken).ConfigureAwait(false);
        return true;
    }
}

public sealed record UserUsage(
    Guid UserId,
    int Executions,
    int Completed,
    int Failed,
    int Cancelled,
    TimeSpan RunTime,
    int Last24Hours,
    DateTimeOffset? LastExecutionAt,
    RuntimeStatus? RuntimeStatus);

public sealed record DailyExecutionCount(DateOnly Date, int Total, int Failed);

public sealed record ExecutionStatistics(
    int Running,
    int Queued,
    int CompletedToday,
    int FailedToday,
    int CancelledToday,
    IReadOnlyList<DailyExecutionCount> Trend);

public sealed record RuntimeSummary(Guid RuntimeId, Guid UserId, RuntimeStatus Status, string ImageVersion, DateTimeOffset? LastActiveAt);
