using System.Collections.Concurrent;

namespace Ymir.VibeMaker.Application.Executions;

/// <summary>
/// 同一 Workspace 同時只執行一個 Agent execution（SA §14：單一 BUSY）。多個 execution 會依序等待，不會互相覆蓋檔案。
/// MVP 為單一 API instance 的 in-memory lock。
/// </summary>
public sealed class WorkspaceExecutionLocks
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var semaphore = _locks.GetOrAdd(workspaceId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
