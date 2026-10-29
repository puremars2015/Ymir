using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.Platform.Identity;
using Ymir.Platform.Notifications;

namespace Ymir.Api.Endpoints;

/// <summary>
/// 站內通知（ADR-0032 §1）：一律以目前登入的使用者為擁有者，別人的通知回 404（SA §12）。
/// 所有登入者（含訪客）都可以使用；即時推送經 <c>/api/events</c> 的 <c>notification</c> 事件。
/// </summary>
internal static class NotificationEndpoints
{
    public const string NotFoundCode = "NOTIFICATION_NOT_FOUND";

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/notifications").WithTags("Notifications").RequireAntiforgeryHeader();
        group.MapGet("/", ListAsync).WithName("ListNotifications").Produces<NotificationPageResponse>();
        group.MapGet("/unread-count", CountUnreadAsync).WithName("CountUnreadNotifications").Produces<NotificationUnreadResponse>();
        group.MapPost("/{id:guid}/read", MarkReadAsync).WithName("MarkNotificationRead").Produces<NotificationUnreadResponse>();
        group.MapPost("/read-all", MarkAllReadAsync).WithName("MarkAllNotificationsRead").Produces<NotificationUnreadResponse>();
        return endpoints;
    }

    private static async Task<NotificationPageResponse> ListAsync(Guid? before, int? take, ICurrentUser currentUser, INotificationService notifications, CancellationToken cancellationToken)
    {
        var page = await notifications.ListAsync(currentUser.UserId, before, take ?? 20, cancellationToken);
        return new NotificationPageResponse([.. page.Items.Select(NotificationResponse.From)], page.HasMore, page.UnreadCount);
    }

    private static async Task<NotificationUnreadResponse> CountUnreadAsync(ICurrentUser currentUser, INotificationService notifications, CancellationToken cancellationToken) =>
        new(await notifications.CountUnreadAsync(currentUser.UserId, cancellationToken));

    private static async Task<IResult> MarkReadAsync(Guid id, ICurrentUser currentUser, INotificationService notifications, CancellationToken cancellationToken) =>
        await notifications.MarkReadAsync(currentUser.UserId, id, cancellationToken)
            ? TypedResults.Ok(new NotificationUnreadResponse(await notifications.CountUnreadAsync(currentUser.UserId, cancellationToken)))
            : ApiProblem.Create(StatusCodes.Status404NotFound, NotFoundCode, "找不到這則通知。");

    private static async Task<NotificationUnreadResponse> MarkAllReadAsync(ICurrentUser currentUser, INotificationService notifications, CancellationToken cancellationToken)
    {
        await notifications.MarkAllReadAsync(currentUser.UserId, cancellationToken);
        return new NotificationUnreadResponse(0);
    }
}

/// <param name="Link">站內相對路徑（由伺服器產生）；null 表示不連到任何頁面。</param>
/// <param name="ReadAt">null 表示未讀。</param>
public sealed record NotificationResponse(Guid Id, NotificationKind Kind, string Title, string? Body, string? Link, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt)
{
    public static NotificationResponse From(NotificationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new(item.Id, item.Kind, item.Title, item.Body, item.Link, item.CreatedAt, item.ReadAt);
    }
}

/// <param name="Items">由新到舊。</param>
/// <param name="HasMore">是否還有更舊的通知（以最後一則的 id 當 <c>before</c> 繼續讀）。</param>
public sealed record NotificationPageResponse(IReadOnlyList<NotificationResponse> Items, bool HasMore, int UnreadCount);

public sealed record NotificationUnreadResponse(int UnreadCount);

/// <summary>SSE <c>notification-read</c>：同一位使用者在其他分頁讀了通知。</summary>
/// <param name="Id">讀取的通知；null 表示全部已讀。</param>
public sealed record NotificationsReadResponse(Guid? Id, int UnreadCount);
