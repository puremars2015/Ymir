using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Contracts.Executions;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Executions;

/// <summary>
/// 服務啟動時的 reconciliation（SA §14）：上次中斷時仍在 RUNNING 的 execution 標成 FAILED 並寫入終止事件；
/// 仍在 QUEUED 的重新放回佇列。
/// </summary>
public sealed class ExecutionReconciler(
    IVibeMakerDbContext db,
    ExecutionEventWriter eventWriter,
    IExecutionDispatcher queue,
    TimeProvider timeProvider,
    ILogger<ExecutionReconciler> logger)
{
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        var interrupted = await db.AgentExecutions.Where(e => e.Status == ExecutionStatus.Running).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var execution in interrupted)
        {
            var now = timeProvider.GetUtcNow();
            const string message = "服務重新啟動，執行已中斷，請重新送出。";
            execution.Fail(ExecutionErrorCodes.AgentRuntimeError, null, now);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            var sequence = (await db.ExecutionEvents.Where(e => e.ExecutionId == execution.Id)
                .MaxAsync(e => (long?)e.Sequence, cancellationToken).ConfigureAwait(false) ?? 0) + 1;
            await eventWriter.AppendAsync(execution.Id, sequence, new ExecutionFailedEvent(ExecutionErrorCodes.AgentRuntimeError, message), cancellationToken)
                .ConfigureAwait(false);
        }

        var queued = await db.AgentExecutions.AsNoTracking().Where(e => e.Status == ExecutionStatus.Queued)
            .OrderBy(e => e.CreatedAt).Select(e => e.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var executionId in queued)
        {
            await queue.EnqueueAsync(executionId, cancellationToken).ConfigureAwait(false);
        }

        if (interrupted.Count > 0 || queued.Count > 0)
        {
            logger.LogWarning("Reconciled executions: {Interrupted} interrupted marked FAILED, {Queued} re-queued", interrupted.Count, queued.Count);
        }
    }
}
