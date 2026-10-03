namespace Ymir.Platform.Auditing;

/// <summary>
/// 稽核紀錄寫入點，與一般 application log 分離（SA §18）。
/// runtime create/start/stop/delete、login、agent execution、admin action 都必須寫入（SA §12）。
/// </summary>
public interface IAuditLog
{
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken);
}
