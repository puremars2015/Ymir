namespace Ymir.Platform.Auditing;

/// <summary>
/// 稽核紀錄的保存期限（Sprint 5 強化）：只供背景清理使用，刪除早於期限的紀錄。
/// 一般程式只能寫入（<see cref="IAuditLog"/>）與查詢（<see cref="IAuditLogQuery"/>）。
/// </summary>
public interface IAuditLogMaintenance
{
    /// <returns>刪除的筆數。</returns>
    Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);
}
