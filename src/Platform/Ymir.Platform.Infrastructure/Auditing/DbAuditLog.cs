using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Ymir.Platform.Auditing;
using Ymir.Platform.Infrastructure.Persistence;

namespace Ymir.Platform.Infrastructure.Auditing;

/// <summary>
/// 寫入 platform.audit_log（SA §18：與 application log 分離）。
/// 使用獨立的 DbContext scope，避免與呼叫端尚未儲存的變更混在同一個 SaveChanges。
/// </summary>
internal sealed class DbAuditLog(IServiceScopeFactory scopeFactory) : IAuditLog
{
    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            db.AuditLog.Add(new AuditLogRecord
            {
                Actor = entry.Actor,
                Action = entry.Action,
                TargetType = entry.TargetType,
                TargetId = entry.TargetId,
                Result = entry.Result.ToString().ToUpperInvariant(),
                Timestamp = entry.Timestamp,
                CorrelationId = entry.CorrelationId ?? Activity.Current?.TraceId.ToString(),
            });
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
