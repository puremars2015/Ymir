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

    /// <summary>不等待：使用者正在執行時回傳 null（閒置停止用來避開執行中的 runtime）。</summary>
    public IDisposable? TryAcquire(Guid userId)
    {
        var semaphore = _locks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        return semaphore.Wait(0) ? new Releaser(semaphore) : null;
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
