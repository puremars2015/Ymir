using Microsoft.EntityFrameworkCore;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Executions;

/// <summary>
/// execution 事件的保存期限（Sprint 5 強化）：事件只用於 SSE 即時顯示與斷線續傳，
/// 對話內容另外保存在 messages（永久保留，SA §15），所以已結束的 execution 的事件可以在一段時間後刪除。
/// 執行中或排隊中的 execution 不刪。
/// </summary>
public sealed class ExecutionEventRetention(IVibeMakerDbContext db)
{
    /// <returns>刪除的事件筆數。</returns>
    public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
        db.ExecutionEvents
            .Where(e => e.CreatedAt < cutoff
                && db.AgentExecutions.Any(x => x.Id == e.ExecutionId
                    && x.Status != ExecutionStatus.Queued
                    && x.Status != ExecutionStatus.Running))
            .ExecuteDeleteAsync(cancellationToken);
}
