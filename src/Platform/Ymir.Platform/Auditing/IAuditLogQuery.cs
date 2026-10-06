namespace Ymir.Platform.Auditing;

/// <summary>
/// 稽核紀錄查詢（Admin 稽核頁，ADR-0010）。寫入仍只經由 <see cref="IAuditLog"/>；
/// 這裡只讀，結果依 Id 由新到舊，以 <see cref="AuditLogFilter.BeforeId"/> 做 keyset 分頁。
/// </summary>
public interface IAuditLogQuery
{
    Task<AuditLogPage> SearchAsync(AuditLogFilter filter, CancellationToken cancellationToken);
}

/// <param name="ActionPrefix">動作前綴，例如 <c>admin.</c>、<c>auth.login</c>。</param>
/// <param name="UserId">操作者或目標是這位使用者。</param>
/// <param name="Result">結果；null 表示全部。</param>
/// <param name="From">起始時間（含）。</param>
/// <param name="To">結束時間（不含）。</param>
/// <param name="BeforeId">只回傳 Id 小於此值的紀錄（下一頁）。</param>
/// <param name="Take">每頁筆數（1～200）。</param>
public sealed record AuditLogFilter(
    string? ActionPrefix = null,
    Guid? UserId = null,
    AuditResult? Result = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    long? BeforeId = null,
    int Take = 50);

public sealed record AuditLogRecordView(
    long Id,
    string Actor,
    string Action,
    string TargetType,
    string TargetId,
    AuditResult Result,
    DateTimeOffset Timestamp,
    string? CorrelationId);

/// <param name="NextBeforeId">還有更多資料時，下一頁的 <see cref="AuditLogFilter.BeforeId"/>；沒有則為 null。</param>
public sealed record AuditLogPage(IReadOnlyList<AuditLogRecordView> Items, long? NextBeforeId);

/// <summary>稽核 actor 字串的格式（<c>user:{guid}</c>）。</summary>
public static class AuditActor
{
    public const string UserPrefix = "user:";

    public static string ForUser(Guid userId) => $"{UserPrefix}{userId:D}";

    public static Guid? TryGetUserId(string actor) =>
        actor is not null && actor.StartsWith(UserPrefix, StringComparison.Ordinal) && Guid.TryParse(actor.AsSpan(UserPrefix.Length), out var id)
            ? id
            : null;
}
