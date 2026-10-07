using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Auditing;
using Ymir.Platform.Infrastructure.Persistence;

namespace Ymir.Platform.Infrastructure.Auditing;

internal sealed class DbAuditLogMaintenance(PlatformDbContext db) : IAuditLogMaintenance
{
    public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
        db.AuditLog.Where(a => a.Timestamp < cutoff).ExecuteDeleteAsync(cancellationToken);
}
