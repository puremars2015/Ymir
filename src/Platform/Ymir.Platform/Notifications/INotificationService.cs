using Ymir.Platform.Messaging;

namespace Ymir.Platform.Notifications;

public sealed record NotificationItem(
    Guid Id,
    NotificationKind Kind,
    string Title,
    string? Body,
    string? Link,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt)
{
    public static NotificationItem From(Notification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return new(notification.Id, notification.Kind, notification.Title, notification.Body, notification.Link, notification.CreatedAt, notification.ReadAt);
    }
}

public sealed record NotificationPage(IReadOnlyList<NotificationItem> Items, bool HasMore, int UnreadCount);

/// <summary>新通知：經使用者事件串流（<c>/api/events</c>）即時送到這位使用者的所有分頁（ADR-0032 §2）。</summary>
public sealed record NotificationCreatedNotification(NotificationItem Notification) : DirectMessageNotification;

/// <summary>通知已讀：同一位使用者的其他分頁更新未讀數。<paramref name="Id"/> 為 null 表示全部已讀。</summary>
public sealed record NotificationsReadNotification(Guid? Id, int UnreadCount) : DirectMessageNotification;

/// <summary>
/// 站內通知（ADR-0032 §1）。一律以呼叫端給的 user id 為擁有者，沒有「通知別人」或廣播；
/// 先寫資料庫再即時推送，推送遺失時前端重新連線讀清單即可補齊。
/// </summary>
public interface INotificationService
{
    Task<NotificationItem> CreateAsync(Guid userId, NotificationKind kind, string title, string? body, string? link, Guid? sourceId, CancellationToken cancellationToken);

    /// <param name="before">上一頁最後一則的 id（較舊的方向）；null 表示最新一頁。</param>
    Task<NotificationPage> ListAsync(Guid userId, Guid? before, int take, CancellationToken cancellationToken);

    Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken);

    /// <returns>找不到（或不是這位使用者的）通知時為 false。</returns>
    Task<bool> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken);

    /// <returns>這次標為已讀的則數。</returns>
    Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>帳號停用或刪除時刪除未讀通知（ADR-0032 §1）。</summary>
    Task<int> DeleteUnreadForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>保存期限（<c>Ymir:Retention:NotificationDays</c>）。</summary>
    Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);
}
