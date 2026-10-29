using Microsoft.Extensions.Options;
using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.Platform.Identity;
using Ymir.Platform.Messaging;
using Ymir.Platform.Users;

namespace Ymir.Api.Endpoints;

/// <summary>
/// 使用者之間的訊息（ADR-0023）：右側聯絡人名單、一對一訊息、已讀與 SSE 即時通知。
/// 一律以目前登入的使用者為其中一方，路徑上的 id 只是「對方」，所以看不到別人之間的訊息（SA §12）。
/// </summary>
internal static class DirectMessageEndpoints
{
    public const string NotFoundCode = "DIRECT_MESSAGE_PEER_NOT_FOUND";

    public static IEndpointRouteBuilder MapDirectMessageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/direct-messages").WithTags("DirectMessages").RequireAuthorization(AuthSetup.MessagingPolicy).RequireAntiforgeryHeader();
        group.MapGet("/contacts", ListContactsAsync).WithName("ListDirectContacts");
        group.MapGet("/events", StreamEvents)
            .WithName("StreamDirectMessageEvents")
            .WithSummary("（舊路徑，請改用 /api/events）SSE：新訊息（message）、已讀（read）、群組訊息（group-message）、群組異動（group-updated）、群組已讀（group-read）；ready 表示已連線，ping 為心跳。");
        group.MapGet("/{peerId:guid}", GetHistoryAsync).WithName("ListDirectMessages").Produces<DirectMessagePageResponse>();
        group.MapPost("/{peerId:guid}", SendAsync)
            .WithName("SendDirectMessage")
            .RequireRateLimiting(AuthSetup.DirectMessageRateLimit)
            .Produces<DirectMessageResponse>(StatusCodes.Status201Created);
        group.MapPost("/{peerId:guid}/read", MarkReadAsync).WithName("MarkDirectMessagesRead").Produces<DirectMessagesReadResponse>();
        return endpoints;
    }

    private static async Task<IReadOnlyList<DirectContactResponse>> ListContactsAsync(string? q, ICurrentUser currentUser, IDirectMessageService messages, CancellationToken cancellationToken)
    {
        var search = q is { Length: > 100 } ? q[..100] : q;
        return [.. (await messages.ListContactsAsync(currentUser.UserId, search, cancellationToken)).Select(DirectContactResponse.From)];
    }

    private static async Task<IResult> GetHistoryAsync(Guid peerId, Guid? before, int? take, ICurrentUser currentUser, IDirectMessageService messages, CancellationToken cancellationToken) =>
        await messages.GetHistoryAsync(currentUser.UserId, peerId, before, take ?? 50, cancellationToken) is { } page
            ? TypedResults.Ok(new DirectMessagePageResponse([.. page.Messages.Select(DirectMessageResponse.From)], page.HasMore))
            : PeerNotFound();

    private static async Task<IResult> SendAsync(Guid peerId, SendDirectMessageRequest request, ICurrentUser currentUser, IDirectMessageService messages, CancellationToken cancellationToken)
    {
        var result = await messages.SendAsync(currentUser.UserId, peerId, request.Text ?? string.Empty, cancellationToken);
        return result.Outcome switch
        {
            DirectMessageSendOutcome.Sent => TypedResults.Created((string?)null, DirectMessageResponse.From(result.Message!)),
            DirectMessageSendOutcome.InvalidBody => ApiProblem.Create(StatusCodes.Status400BadRequest, "DIRECT_MESSAGE_INVALID", $"訊息不可空白，且不能超過 {DirectMessage.BodyMaxLength} 字。"),
            _ => PeerNotFound(),
        };
    }

    private static async Task<IResult> MarkReadAsync(Guid peerId, ICurrentUser currentUser, IDirectMessageService messages, CancellationToken cancellationToken) =>
        await messages.MarkReadAsync(currentUser.UserId, peerId, cancellationToken) is { } read
            ? TypedResults.Ok(DirectMessagesReadResponse.From(read))
            : PeerNotFound();

    /// <summary>
    /// 舊路徑（ADR-0023）：保留一個版本作為 <c>/api/events</c> 的相容別名（ADR-0032 §2），只送私訊與群組事件。
    /// </summary>
    private static Microsoft.AspNetCore.Http.HttpResults.ServerSentEventsResult<string> StreamEvents(
        HttpContext httpContext,
        ICurrentUser currentUser,
        IDirectMessageHub hub,
        IUserDirectory users,
        IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers["X-Accel-Buffering"] = "no"; // 反向代理不要緩衝串流
        return TypedResults.ServerSentEvents(UserEventEndpoints.ReadEventsAsync(currentUser.UserId, hub, users, jsonOptions.Value.SerializerOptions, messagingOnly: true, cancellationToken));
    }

    private static IResult PeerNotFound() => ApiProblem.Create(StatusCodes.Status404NotFound, NotFoundCode, "找不到這位使用者。");
}

/// <param name="UnreadCount">對方傳給我、我還沒讀的則數。</param>
/// <param name="LastMessageAt">最後一則往來訊息的時間；沒有往來時為 null。</param>
public sealed record DirectContactResponse(Guid UserId, string DisplayName, string? AccountName, string? Department, int UnreadCount, DateTimeOffset? LastMessageAt)
{
    public static DirectContactResponse From(DirectContact contact) =>
        new(contact.UserId, contact.DisplayName, contact.AccountName, contact.Department, contact.UnreadCount, contact.LastMessageAt);
}

/// <param name="ReadAt">收件者讀取的時間；null 表示未讀。</param>
public sealed record DirectMessageResponse(Guid Id, Guid SenderId, Guid RecipientId, string Body, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt)
{
    public static DirectMessageResponse From(DirectMessageItem message) =>
        new(message.Id, message.SenderId, message.RecipientId, message.Body, message.CreatedAt, message.ReadAt);
}

/// <param name="Messages">由舊到新。</param>
/// <param name="HasMore">是否還有更舊的訊息（以第一則的 id 當 <c>before</c> 繼續讀）。</param>
public sealed record DirectMessagePageResponse(IReadOnlyList<DirectMessageResponse> Messages, bool HasMore);

public sealed record SendDirectMessageRequest(string? Text);

/// <param name="ReaderId">讀取的人。</param>
/// <param name="PeerId">被讀取訊息的寄件者。</param>
/// <param name="Count">這次標為已讀的則數。</param>
public sealed record DirectMessagesReadResponse(Guid ReaderId, Guid PeerId, DateTimeOffset ReadAt, int Count)
{
    public static DirectMessagesReadResponse From(DirectMessagesRead read) => new(read.ReaderId, read.PeerId, read.ReadAt, read.Count);
}

/// <param name="Removed">自己被移除、群組被封存：前端從清單拿掉這個群組。</param>
public sealed record GroupUpdatedResponse(Guid GroupId, bool Removed);
