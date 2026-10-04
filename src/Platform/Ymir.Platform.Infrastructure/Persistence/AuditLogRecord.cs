namespace Ymir.Platform.Infrastructure.Persistence;

/// <summary>platform.audit_log 的資料列。</summary>
internal sealed class AuditLogRecord
{
    public long Id { get; set; }

    public string Actor { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string TargetType { get; set; } = string.Empty;

    public string TargetId { get; set; } = string.Empty;

    public string Result { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; }

    public string? CorrelationId { get; set; }
}
