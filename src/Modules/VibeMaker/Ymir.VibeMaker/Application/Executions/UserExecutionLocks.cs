using System.Collections.Concurrent;

namespace Ymir.VibeMaker.Application.Executions;

/// <summary>
/// 同一使用者同時只執行一個 Agent execution（SA §14：單一 BUSY）。一個使用者只有一個 container（ADR-0007），
/// 共用 CPU / 記憶體限制，多個 execution 依序等待。MVP 為單一 API instance 的 in-memory lock。
/// </summary>
public sealed class UserExecutionLocks
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(Guid userId, CancellationToken cancellationToken)
    {
        var semaphore = _locks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
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
