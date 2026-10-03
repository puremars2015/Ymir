using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Ymir.VibeMaker.Application.Executions;

namespace Ymir.VibeMaker.Infrastructure.Executions;

/// <summary>單一 API instance 的 in-memory 佇列。重新啟動時遺失的項目由 <see cref="ExecutionReconciler"/> 從資料庫補回。</summary>
internal sealed class ChannelExecutionDispatcher : IExecutionDispatcher
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(Guid executionId, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(executionId, cancellationToken);

    public async IAsyncEnumerable<Guid> DequeueAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var executionId in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return executionId;
        }
    }
}
