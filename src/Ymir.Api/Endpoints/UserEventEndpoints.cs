using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Ymir.Api.Auth;
using Ymir.Platform.Identity;
using Ymir.Platform.Messaging;
using Ymir.Platform.Notifications;
using Ymir.Platform.Users;

namespace Ymir.Api.Endpoints;

/// <summary>
/// 使用者事件串流（ADR-0032 §2）：每個分頁只開一條 SSE，同時傳送通知（所有登入者，含訪客）與私訊 / 群組事件
/// （只給可以使用訊息的角色，ADR-0027）。取代 <c>/api/direct-messages/events</c>（保留一版作為別名）。
/// </summary>
internal static class UserEventEndpoints
{
    /// <summary>
    /// SSE 心跳間隔：Cloudflare 等代理會切斷閒置超過 100 秒的連線（ADR-0006）；
    /// 每次心跳也重新確認帳號狀態與角色，停用的帳號不會一直收到事件、改成訪客後不再收到訊息（ADR-0009、ADR-0027）。
    /// </summary>
    internal static TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(25);

    public static IEndpointRouteBuilder MapUserEventEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/events", StreamEvents)
            .WithTags("Events")
            .RequireAntiforgeryHeader()
            .WithName("StreamUserEvents")
            .WithSummary("SSE：通知（notification、notification-read）；可以使用訊息的角色另有私訊（message、read）與群組（group-message、group-updated、group-read）。ready 表示已連線，ping 為心跳。");
        return endpoints;
    }

    private static Microsoft.AspNetCore.Http.HttpResults.ServerSentEventsResult<string> StreamEvents(
        HttpContext httpContext,
        ICurrentUser currentUser,
        IDirectMessageHub hub,
        IUserDirectory users,
        IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers["X-Accel-Buffering"] = "no"; // 反向代理不要緩衝串流
        return TypedResults.ServerSentEvents(ReadEventsAsync(currentUser.UserId, hub, users, jsonOptions.Value.SerializerOptions, messagingOnly: false, cancellationToken));
    }

    /// <param name="messagingOnly">舊的私訊串流：只送私訊與群組事件，訪客一律結束。</param>
    internal static async IAsyncEnumerable<SseItem<string>> ReadEventsAsync(
        Guid userId,
        IDirectMessageHub hub,
        IUserDirectory users,
        JsonSerializerOptions jsonOptions,
        bool messagingOnly,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // 先訂閱再送 ready，ready 之後發生的事件一定收得到；前端在 ready 時重新讀取資料，補齊斷線期間的事件。
        using var subscription = hub.Subscribe(userId);
        var canMessage = await CanUseMessagingAsync(userId, users, cancellationToken);
        if (canMessage is null || (messagingOnly && canMessage == false))
        {
            yield break;
        }

        yield return new SseItem<string>("{}", "ready");

        // 結束時（斷線、帳號停用）先取消還在等待的讀取再釋放：非同步迭代器在 MoveNextAsync 進行中被釋放會拋 NotSupportedException。
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var events = subscription.ReadAllAsync(reading.Token).GetAsyncEnumerator(reading.Token);
        var next = events.MoveNextAsync().AsTask();
        try
        {
            while (true)
            {
                using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var delay = Task.Delay(HeartbeatInterval, heartbeat.Token);
                if (await Task.WhenAny(next, delay) != next)
                {
                    canMessage = await CanUseMessagingAsync(userId, users, cancellationToken);
                    if (canMessage is null || (messagingOnly && canMessage == false))
                    {
                        yield break;
                    }

                    yield return new SseItem<string>("{}", "ping");
                    continue;
                }

                await heartbeat.CancelAsync();
                if (!await next)
                {
                    yield break;
                }

                var item = ToSseItem(events.Current, jsonOptions, canMessage == true, includeNotifications: !messagingOnly);
                next = events.MoveNextAsync().AsTask();
                if (item is not null)
                {
                    yield return item.Value;
                }
            }
        }
        finally
        {
            await reading.CancelAsync();
            await WaitQuietlyAsync(next);
            await events.DisposeAsync();
        }
    }

    private static async Task WaitQuietlyAsync(Task pending)
    {
        try
        {
            await pending;
        }
        catch (OperationCanceledException)
        {
            // 預期：上面剛取消了讀取。
        }
    }

    /// <returns>null：帳號不存在或不是 Active，結束串流。</returns>
    private static async Task<bool?> CanUseMessagingAsync(Guid userId, IUserDirectory users, CancellationToken cancellationToken) =>
        await users.FindAsync(userId, cancellationToken) is { Status: UserStatus.Active } user
            ? user.Role != UserRole.Guest
            : null;

    private static SseItem<string>? ToSseItem(DirectMessageNotification notification, JsonSerializerOptions jsonOptions, bool messaging, bool includeNotifications) => notification switch
    {
        NotificationCreatedNotification created when includeNotifications =>
            new(JsonSerializer.Serialize(NotificationResponse.From(created.Notification), jsonOptions), "notification"),
        NotificationsReadNotification read when includeNotifications =>
            new(JsonSerializer.Serialize(new NotificationsReadResponse(read.Id, read.UnreadCount), jsonOptions), "notification-read"),
        DirectMessageSentNotification sent when messaging =>
            new(JsonSerializer.Serialize(DirectMessageResponse.From(sent.Message), jsonOptions), "message"),
        DirectMessagesReadNotification read when messaging =>
            new(JsonSerializer.Serialize(DirectMessagesReadResponse.From(read.Read), jsonOptions), "read"),
        // 群組聊天（ADR-0026 §1）：同一條連線傳送群組的訊息、Bot 回覆更新、群組異動與已讀。
        GroupMessageNotification group when messaging =>
            new(JsonSerializer.Serialize(GroupMessageResponse.From(group.Message), jsonOptions), "group-message"),
        GroupUpdatedNotification updated when messaging =>
            new(JsonSerializer.Serialize(new GroupUpdatedResponse(updated.GroupId, updated.Removed), jsonOptions), "group-updated"),
        GroupReadNotification groupRead when messaging =>
            new(JsonSerializer.Serialize(new GroupReadResponse(groupRead.Read.GroupId, groupRead.Read.ReaderId, groupRead.Read.ReadAt), jsonOptions), "group-read"),
        _ => null,
    };
}
