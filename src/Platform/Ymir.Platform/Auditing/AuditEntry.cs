namespace Ymir.Platform.Auditing;

/// <summary>稽核紀錄，欄位為 SA §18 的最低要求。</summary>
public sealed record AuditEntry(
    string Actor,
    string Action,
    string TargetType,
    string TargetId,
    AuditResult Result,
    DateTimeOffset Timestamp,
    string? CorrelationId);

public enum AuditResult
{
    Success = 0,
    Failure = 1,
    Denied = 2,
}
