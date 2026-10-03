using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Contracts.Executions;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Executions;

/// <summary>先寫 execution_events 再發佈到 bus，確保斷線重連時資料庫裡一定有（開發規劃 §5）。</summary>
public sealed class ExecutionEventWriter(IVibeMakerDbContext db, IExecutionEventBus bus, TimeProvider timeProvider)
{
    public static bool IsTerminal(string eventType) =>
        eventType is ExecutionEventNames.ExecutionCompleted or ExecutionEventNames.ExecutionFailed or ExecutionEventNames.ExecutionCancelled;

    public async Task<StoredExecutionEvent> AppendAsync(Guid executionId, long sequence, ExecutionEvent executionEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executionEvent);
        var data = executionEvent.ToJson();
        db.ExecutionEvents.Add(ExecutionEventRecord.Create(executionId, sequence, executionEvent.EventName, data, timeProvider.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var stored = new StoredExecutionEvent(executionId, sequence, executionEvent.EventName, data, IsTerminal(executionEvent.EventName));
        bus.Publish(stored);
        return stored;
    }
}
