using Microsoft.Extensions.Logging;
using Ymir.Platform.Auditing;

namespace Ymir.Platform.Infrastructure.Auditing;

/// <summary>
/// Sprint 0 的暫時實作：寫到獨立的 <c>Ymir.Audit</c> log category。
/// Sprint 1 改為寫入 <c>platform.audit_log</c> 資料表。
/// </summary>
internal sealed class LoggerAuditLog(ILoggerFactory loggerFactory) : IAuditLog
{
    private readonly ILogger _logger = loggerFactory.CreateLogger("Ymir.Audit");

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "AUDIT {Actor} {Action} {TargetType}/{TargetId} {Result} at {Timestamp} correlation={CorrelationId}",
            entry.Actor, entry.Action, entry.TargetType, entry.TargetId, entry.Result, entry.Timestamp, entry.CorrelationId);
        return Task.CompletedTask;
    }
}
