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

    /// <summary>停止指定使用者的 runtime（不刪除檔案）；沒有 runtime 時回傳 false。</summary>
    public async Task<bool> StopRuntimeAsync(Guid userId, string actor, CancellationToken cancellationToken)
    {
        var runtimeId = await db.AgentRuntimes.AsNoTracking()
            .Where(r => r.UserId == userId && r.Status != RuntimeStatus.Deleted)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (runtimeId is not { } id)
        {
            return false;
        }

        await runtimes.StopAsync(id, cancellationToken).ConfigureAwait(false);
        var record = await db.AgentRuntimes.SingleAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);
        record.MarkStatus(RuntimeStatus.Stopped, timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditLog.WriteAsync(
            new AuditEntry(actor, "admin.runtime.stop", "user", userId.ToString("D"), AuditResult.Success, timeProvider.GetUtcNow(), null),
            cancellationToken).ConfigureAwait(false);
        return true;
    }
}

public sealed record DailyExecutionCount(DateOnly Date, int Total, int Failed);

public sealed record ExecutionStatistics(
    int Running,
    int Queued,
    int CompletedToday,
    int FailedToday,
    int CancelledToday,
    IReadOnlyList<DailyExecutionCount> Trend);

public sealed record RuntimeSummary(Guid RuntimeId, Guid UserId, RuntimeStatus Status, string ImageVersion, DateTimeOffset? LastActiveAt);
