using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Ymir.Platform.Messaging;

namespace Ymir.Platform.Infrastructure.Messaging;

/// <summary>
/// 程序內的即時通知（ADR-0023）。Ymir 目前只部署一個 API 程序（ADR-0008），所以不需要外部訊息匯流排；
/// 要水平擴充時改成以資料庫或 Redis 傳遞，介面不變。
/// </summary>
internal sealed class InMemoryDirectMessageHub : IDirectMessageHub
{
    /// <summary>每條連線最多暫存的通知數；瀏覽器太慢時丟掉最舊的，重新連線時前端會重新讀取資料補齊。</summary>
    private const int Capacity = 200;

    private readonly ConcurrentDictionary<Guid, ImmutableList<Channel<DirectMessageNotification>>> subscribers = new();

    public IDirectMessageSubscription Subscribe(Guid userId)
    {
        var channel = Channel.CreateBounded<DirectMessageNotification>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        subscribers.AddOrUpdate(userId, _ => [channel], (_, list) => list.Add(channel));
        return new Subscription(this, userId, channel);
    }

    public void Publish(Guid userId, DirectMessageNotification notification)
    {
        if (subscribers.TryGetValue(userId, out var list))
        {
            foreach (var channel in list)
            {
                channel.Writer.TryWrite(notification);
            }
        }
    }

    /// <summary>目前的連線數（測試用）。</summary>
    internal int ConnectionCount(Guid userId) => subscribers.TryGetValue(userId, out var list) ? list.Count : 0;

    private void Unsubscribe(Guid userId, Channel<DirectMessageNotification> channel)
    {
        while (subscribers.TryGetValue(userId, out var list))
        {
            var next = list.Remove(channel);
            var done = next.IsEmpty
                ? subscribers.TryRemove(new KeyValuePair<Guid, ImmutableList<Channel<DirectMessageNotification>>>(userId, list))
                : subscribers.TryUpdate(userId, next, list);
            if (done)
            {
                break;
            }
        }

        channel.Writer.TryComplete();
    }

    private sealed class Subscription(InMemoryDirectMessageHub hub, Guid userId, Channel<DirectMessageNotification> channel) : IDirectMessageSubscription
    {
        public async IAsyncEnumerable<DirectMessageNotification> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var notification in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return notification;
            }
        }

        public void Dispose() => hub.Unsubscribe(userId, channel);
    }
}
