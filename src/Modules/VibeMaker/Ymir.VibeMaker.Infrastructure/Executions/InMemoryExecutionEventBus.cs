using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Ymir.VibeMaker.Application.Executions;

namespace Ymir.VibeMaker.Infrastructure.Executions;

/// <summary>單一 API instance 的事件推送；多 instance 時改用 Redis pub/sub 等實作（開發規劃 §5）。</summary>
internal sealed class InMemoryExecutionEventBus : IExecutionEventBus
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Subscription, byte>> _subscribers = new();

    public void Publish(StoredExecutionEvent executionEvent)
    {
        ArgumentNullException.ThrowIfNull(executionEvent);
        if (_subscribers.TryGetValue(executionEvent.ExecutionId, out var subscriptions))
        {
            foreach (var subscription in subscriptions.Keys)
            {
                subscription.Write(executionEvent);
            }
        }
    }

    public ExecutionEventSubscription Subscribe(Guid executionId)
    {
        var subscription = new Subscription(this, executionId);
        _subscribers.GetOrAdd(executionId, _ => new ConcurrentDictionary<Subscription, byte>()).TryAdd(subscription, 0);
        return subscription;
    }

    private void Remove(Subscription subscription)
    {
        if (_subscribers.TryGetValue(subscription.ExecutionId, out var subscriptions))
        {
            subscriptions.TryRemove(subscription, out _);
            if (subscriptions.IsEmpty)
            {
                _subscribers.TryRemove(new KeyValuePair<Guid, ConcurrentDictionary<Subscription, byte>>(subscription.ExecutionId, subscriptions));
            }
        }
    }

    private sealed class Subscription(InMemoryExecutionEventBus bus, Guid executionId) : ExecutionEventSubscription
    {
        private readonly Channel<StoredExecutionEvent> _channel = Channel.CreateUnbounded<StoredExecutionEvent>(new UnboundedChannelOptions { SingleReader = true });

        public Guid ExecutionId { get; } = executionId;

        public void Write(StoredExecutionEvent executionEvent) => _channel.Writer.TryWrite(executionEvent);

        public override async IAsyncEnumerable<StoredExecutionEvent> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var executionEvent in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return executionEvent;
            }
        }

        protected override void Dispose(bool disposing)
        {
            bus.Remove(this);
            _channel.Writer.TryComplete();
        }
    }
}
