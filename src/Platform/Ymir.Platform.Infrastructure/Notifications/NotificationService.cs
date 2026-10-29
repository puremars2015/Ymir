using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Infrastructure.Persistence;
using Ymir.Platform.Messaging;
using Ymir.Platform.Notifications;

namespace Ymir.Platform.Infrastructure.Notifications;

internal sealed class NotificationService(PlatformDbContext db, IDirectMessageHub hub, TimeProvider timeProvider) : INotificationService
{
    internal const int MaxPageSize = 50;

    public async Task<NotificationItem> CreateAsync(Guid userId, NotificationKind kind, string title, string? body, string? link, Guid? sourceId, CancellationToken cancellationToken)
    {
        var notification = Notification.Create(userId, kind, title, body, link, sourceId, timeProvider.GetUtcNow());
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var item = NotificationItem.From(notification);
        hub.Publish(userId, new NotificationCreatedNotification(item));
        return item;
    }

    public async Task<NotificationPage> ListAsync(Guid userId, Guid? before, int take, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(take, 1, MaxPageSize);
        var query = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        if (before is { } cursor)
        {
            // 游標只在自己的通知裡查；SQL Server 的 uniqueidentifier 排序與 Guid v7 的時間順序不同，所以用 CreatedAt 當游標。
            var cursorAt = await query.Where(n => n.Id == cursor).Select(n => (DateTimeOffset?)n.CreatedAt).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (cursorAt is null)
            {
                return new NotificationPage([], false, await CountUnreadAsync(userId, cancellationToken).ConfigureAwait(false));
            }

            query = query.Where(n => n.CreatedAt < cursorAt.Value);
        }

        var rows = await query.OrderByDescending(n => n.CreatedAt).Take(size + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        var unread = await CountUnreadAsync(userId, cancellationToken).ConfigureAwait(false);
        return new NotificationPage([.. rows.Take(size).Select(NotificationItem.From)], rows.Count > size, unread);
    }

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null, cancellationToken);

    public async Task<bool> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken)
    {
        var notification = await db.Notifications.SingleOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (notification is null)
        {
            return false;
        }

        if (notification.ReadAt is null)
        {
            notification.MarkRead(timeProvider.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            hub.Publish(userId, new NotificationsReadNotification(notificationId, await CountUnreadAsync(userId, cancellationToken).ConfigureAwait(false)));
        }

        return true;
    }

    public async Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var count = await db.Notifications
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), cancellationToken)
            .ConfigureAwait(false);
        if (count > 0)
        {
            hub.Publish(userId, new NotificationsReadNotification(null, 0));
        }

        return count;
    }

    public Task<int> DeleteUnreadForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Notifications.Where(n => n.UserId == userId && n.ReadAt == null).ExecuteDeleteAsync(cancellationToken);

    public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
        db.Notifications.Where(n => n.CreatedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
}
