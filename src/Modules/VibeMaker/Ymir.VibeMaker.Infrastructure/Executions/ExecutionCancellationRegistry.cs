using System.Collections.Concurrent;
using Ymir.VibeMaker.Application.Executions;

namespace Ymir.VibeMaker.Infrastructure.Executions;

internal sealed class ExecutionCancellationRegistry : IExecutionCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();

    public CancellationTokenSource Register(Guid executionId)
    {
        var source = new CancellationTokenSource();
        _running[executionId] = source;
        return source;
    }

    public void Unregister(Guid executionId) => _running.TryRemove(executionId, out _);

    public bool TryCancel(Guid executionId)
    {
        if (!_running.TryGetValue(executionId, out var source))
        {
            return false;
        }

        try
        {
            source.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }
}
