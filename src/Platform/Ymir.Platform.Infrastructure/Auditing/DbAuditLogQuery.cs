using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Auditing;
using Ymir.Platform.Infrastructure.Persistence;

namespace Ymir.Platform.Infrastructure.Auditing;

internal sealed class DbAuditLogQuery(PlatformDbContext db) : IAuditLogQuery
{
    public const int MaxTake = 200;

    public async Task<AuditLogPage> SearchAsync(AuditLogFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var take = Math.Clamp(filter.Take, 1, MaxTake);
        var query = db.AuditLog.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.ActionPrefix))
        {
            var prefix = filter.ActionPrefix.Trim();
            query = query.Where(a => a.Action.StartsWith(prefix));
        }

        if (filter.UserId is { } userId)
        {
            var actor = AuditActor.ForUser(userId);
            var target = userId.ToString("D");
            query = query.Where(a => a.Actor == actor || (a.TargetType == "user" && a.TargetId == target));
        }

        if (filter.Result is { } result)
        {
            var stored = result.ToString().ToUpperInvariant();
            query = query.Where(a => a.Result == stored);
        }

        if (filter.From is { } from)
        {
            query = query.Where(a => a.Timestamp >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(a => a.Timestamp < to);
        }

        if (filter.BeforeId is { } beforeId)
        {
            query = query.Where(a => a.Id < beforeId);
        }

        // 多取一筆判斷是否還有下一頁
        var rows = await query
            .OrderByDescending(a => a.Id)
            .Take(take + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = rows.Take(take).Select(ToView).ToList();
        var next = rows.Count > take ? items[^1].Id : (long?)null;
        return new AuditLogPage(items, next);
    }

    private static AuditLogRecordView ToView(AuditLogRecord record) => new(
        record.Id,
        record.Actor,
        record.Action,
        record.TargetType,
        record.TargetId,
        Enum.TryParse<AuditResult>(record.Result, ignoreCase: true, out var result) ? result : AuditResult.Failure,
        record.Timestamp,
        record.CorrelationId);
}
