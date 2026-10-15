using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.Platform.Identity;
using Ymir.Platform.Messaging;
using Ymir.VibeMaker.Application.Attachments;
using Ymir.VibeMaker.Application.Executions;
using Ymir.VibeMaker.Application.Groups;
using Ymir.VibeMaker.Contracts.Attachments;
using Ymir.VibeMaker.Contracts.Executions;

namespace Ymir.Api.Endpoints;

/// <summary>
/// 群組聊天與 @bot（ADR-0026）。跨模組組合在 API 層：群組與訊息在 Platform，群組 Agent 的執行在 Vibe Maker。
/// 每個端點都先確認目前使用者是未封存群組的成員；非成員、不存在、已封存一律回 404。
/// </summary>
internal static class ChatGroupEndpoints
{
    public const string NotFoundCode = "CHAT_GROUP_NOT_FOUND";

    public static IEndpointRouteBuilder MapChatGroupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/chat-groups").WithTags("ChatGroups").RequireAuthorization(AuthSetup.MessagingPolicy).RequireAntiforgeryHeader();
        group.MapGet("/", ListAsync).WithName("ListChatGroups").Produces<List<ChatGroupSummaryResponse>>();
        group.MapPost("/", CreateAsync).WithName("CreateChatGroup").Produces<ChatGroupResponse>(StatusCodes.Status201Created);
        group.MapGet("/{groupId:guid}", GetAsync).WithName("GetChatGroup").Produces<ChatGroupResponse>();
        group.MapPut("/{groupId:guid}", RenameAsync).WithName("RenameChatGroup").Produces<ChatGroupResponse>();
        group.MapDelete("/{groupId:guid}", ArchiveAsync).WithName("ArchiveChatGroup").Produces(StatusCodes.Status204NoContent);
        group.MapPost("/{groupId:guid}/members", AddMembersAsync).WithName("AddChatGroupMembers").Produces<ChatGroupResponse>();
        group.MapDelete("/{groupId:guid}/members/{userId:guid}", RemoveMemberAsync).WithName("RemoveChatGroupMember").Produces(StatusCodes.Status204NoContent);
        group.MapGet("/{groupId:guid}/messages", GetMessagesAsync).WithName("ListChatGroupMessages").Produces<GroupMessagePageResponse>();
        group.MapPost("/{groupId:guid}/messages", SendAsync)
            .WithName("SendChatGroupMessage")
            .RequireRateLimiting(AuthSetup.DirectMessageRateLimit)
            .Produces<SendGroupMessageResponse>(StatusCodes.Status201Created);
        group.MapPost("/{groupId:guid}/attachments", UploadAttachmentAsync)
            .WithName("UploadChatGroupAttachment")
            .WithSummary("上傳檔案給群組的 Agent（body 即檔案內容）；回傳的 id 放進 @bot 訊息的 attachmentIds。")
            .Accepts<Stream>("application/octet-stream")
            .Produces<AttachmentResponse>(StatusCodes.Status201Created);
        group.MapPost("/{groupId:guid}/read", MarkReadAsync).WithName("MarkChatGroupRead").Produces<GroupReadResponse>();
        group.MapGet("/{groupId:guid}/executions/{executionId:guid}/events", StreamBotEventsAsync)
            .WithName("StreamChatGroupBotEvents")
            .WithSummary("SSE：群組 @bot 執行的即時事件（與 /api/executions/{id}/events 相同格式），只限群組成員。");
        return endpoints;
    }

    private static async Task<List<ChatGroupSummaryResponse>> ListAsync(ICurrentUser currentUser, IChatGroupService groups, CancellationToken cancellationToken) =>
        [.. (await groups.ListAsync(currentUser.UserId, cancellationToken)).Select(ChatGroupSummaryResponse.From)];

    private static async Task<IResult> CreateAsync(CreateChatGroupRequest request, ICurrentUser currentUser, IChatGroupService groups, CancellationToken cancellationToken) =>
        ToResult(await groups.CreateAsync(currentUser.UserId, request.Name ?? string.Empty, request.MemberIds ?? [], cancellationToken), created: true);

    private static async Task<IResult> GetAsync(Guid groupId, ICurrentUser currentUser, IChatGroupService groups, CancellationToken cancellationToken) =>
        await groups.GetAsync(currentUser.UserId, groupId, cancellationToken) is { } detail ? TypedResults.Ok(ChatGroupResponse.From(detail)) : GroupNotFound();

    private static async Task<IResult> RenameAsync(Guid groupId, RenameChatGroupRequest request, ICurrentUser currentUser, IChatGroupService groups, CancellationToken cancellationToken) =>
        ToResult(await groups.RenameAsync(currentUser.UserId, groupId, request.Name ?? string.Empty, cancellationToken));

    private static async Task<IResult> ArchiveAsync(Guid groupId, ICurrentUser currentUser, IChatGroupService groups, CancellationToken cancellationToken) =>
        ToResult(await groups.ArchiveAsync(currentUser.UserId, groupId, cancellationToken));

    private static async Task<IResult> AddMembersAsync(Guid groupId, AddChatGroupMembersRequest request, ICurrentUser currentUser, IChatGroupService groups, CancellationToken cancellationToken) =>
        ToResult(await groups.AddMembersAsync(currentUser.UserId, groupId, request.UserIds ?? [], cancellationToken));

    private static async Task<IResult> RemoveMemberAsync(Guid groupId, Guid userId, ICurrentUser currentUser, IChatGroupService groups, CancellationToken cancellationToken) =>
        ToResult(await groups.RemoveMemberAsync(currentUser.UserId, groupId, userId, cancellationToken));

    private static async Task<IResult> GetMessagesAsync(Guid groupId, Guid? before, int? take, ICurrentUser currentUser, IChatGroupService groups, CancellationToken cancellationToken) =>
        await groups.GetMessagesAsync(currentUser.UserId, groupId, before, take ?? 50, cancellationToken) is { } page
            ? TypedResults.Ok(new GroupMessagePageResponse([.. page.Messages.Select(GroupMessageResponse.From)], page.HasMore))
            : GroupNotFound();

    /// <summary>
    /// 送出訊息；以 <c>@bot</c> 開頭時另外為目前使用者建立群組 Agent 的執行（ADR-0026 §2）。
    /// 訊息本身一定保存並通知成員；@bot 無法執行（忙碌、配額、模型）時在回應中說明，不影響訊息。
    /// </summary>
    private static async Task<IResult> SendAsync(
        Guid groupId,
        SendGroupMessageRequest request,
        ICurrentUser currentUser,
        IChatGroupService groups,
        ExecutionService executions,
        AttachmentService attachments,
        CancellationToken cancellationToken)
    {
        var text = request.Text ?? string.Empty;
        var botRequest = GroupPromptBuilder.ParseBotRequest(text);
        if (botRequest is { Length: 0 })
        {
            return ApiProblem.Create(StatusCodes.Status400BadRequest, "GROUP_BOT_REQUEST_REQUIRED", "請在 @bot 後面輸入要問的內容。");
        }

        // 附件是給 Agent 的：只能隨 @bot 訊息送出。先確認都可用，群組訊息才寫上檔名。
        var attachmentIds = request.AttachmentIds ?? [];
        if (attachmentIds.Count > 0)
        {
            if (botRequest is null)
            {
                return ApiProblem.Create(StatusCodes.Status400BadRequest, "GROUP_ATTACHMENT_REQUIRES_BOT", "附件只能隨 @bot 訊息送給 Agent。");
            }

            if (!await groups.IsMemberAsync(currentUser.UserId, groupId, cancellationToken))
            {
                return GroupNotFound();
            }

            var fileNames = await attachments.FindPendingGroupFileNamesAsync(groupId, attachmentIds, cancellationToken);
            if (fileNames is null)
            {
                return ApiProblem.Create(StatusCodes.Status400BadRequest, ExecutionErrorCodes.AttachmentNotAvailable, "附加的檔案無法使用，請重新選擇檔案。");
            }

            text = GroupAttachmentNote.Append(text, fileNames, DirectMessage.BodyMaxLength);
        }

        // 送出前先取背景，不含這次的訊息本身。
        var context = botRequest is null ? [] : await groups.GetRecentAsync(groupId, GroupPromptBuilder.MaxContextMessages, cancellationToken);
        var sent = await groups.SendAsync(currentUser.UserId, groupId, text, cancellationToken);
        switch (sent.Outcome)
        {
            case GroupMessageSendOutcome.InvalidBody:
                return ApiProblem.Create(StatusCodes.Status400BadRequest, "GROUP_MESSAGE_INVALID", $"訊息不可空白，且不能超過 {DirectMessage.BodyMaxLength} 字。");
            case GroupMessageSendOutcome.NotFound:
                return GroupNotFound();
        }

        var message = GroupMessageResponse.From(sent.Message!);
        if (botRequest is null)
        {
            return TypedResults.Created((string?)null, new SendGroupMessageResponse(message, null, null));
        }

        var detail = await groups.GetAsync(currentUser.UserId, groupId, cancellationToken);
        if (detail is null)
        {
            return GroupNotFound();
        }

        var callerName = sent.Message!.SenderName ?? "成員";
        var prompt = GroupPromptBuilder.Build(
            detail.Name,
            callerName,
            botRequest,
            [.. context.Select(m => new GroupContextLine(m.SenderKind == GroupSenderKind.Bot ? "@bot" : m.SenderName ?? "成員", m.Body, m.CreatedAt, m.SenderKind == GroupSenderKind.Bot))]);
        var result = await executions.SubmitForGroupAsync(groupId, detail.Name, $"{callerName}：{botRequest}", prompt, request.ClientRequestId ?? Guid.NewGuid(), attachmentIds, cancellationToken);
        if (result.Outcome != SubmitMessageOutcome.Accepted)
        {
            var problem = result.Outcome switch
            {
                SubmitMessageOutcome.Conflict => "@bot 正在回覆其他人的請求，請稍候再試。",
                SubmitMessageOutcome.QuotaExceeded => result.Message ?? "已達使用上限。",
                SubmitMessageOutcome.ModelNotAvailable => "目前沒有可用的模型，請洽管理員。",
                SubmitMessageOutcome.AttachmentNotAvailable => "附加的檔案無法使用，請重新選擇檔案。",
                _ => "無法呼叫 @bot。",
            };
            return TypedResults.Created((string?)null, new SendGroupMessageResponse(message, null, problem));
        }

        var bot = await groups.AddBotPendingAsync(groupId, currentUser.UserId, result.Response!.ExecutionId, cancellationToken);
        return TypedResults.Created((string?)null, new SendGroupMessageResponse(message, GroupMessageResponse.From(bot), null));
    }

    /// <summary>
    /// 成員上傳檔案給群組的 Agent：寫進群組 Agent 對話的工作目錄（runtime 擁有者 = 群組 id），只有上傳者能在自己的 @bot 訊息使用。
    /// 非成員、不存在、已封存一律 404（先確認成員資格才讀取 body）。
    /// </summary>
    private static async Task<IResult> UploadAttachmentAsync(
        Guid groupId,
        string? fileName,
        HttpContext context,
        ICurrentUser currentUser,
        IChatGroupService groups,
        AttachmentService attachments,
        CancellationToken cancellationToken)
    {
        if (await groups.GetAsync(currentUser.UserId, groupId, cancellationToken) is not { } detail)
        {
            return GroupNotFound();
        }

        return await AttachmentEndpoints.ReceiveAsync(
            context,
            temp => attachments.UploadForGroupAsync(groupId, detail.Name, fileName, temp, cancellationToken),
            GroupNotFound,
            cancellationToken);
    }

    private static async Task<IResult> MarkReadAsync(Guid groupId, ICurrentUser currentUser, IChatGroupService groups, CancellationToken cancellationToken) =>
        await groups.MarkReadAsync(currentUser.UserId, groupId, cancellationToken) is { } read
            ? TypedResults.Ok(new GroupReadResponse(read.GroupId, read.ReaderId, read.ReadAt))
            : GroupNotFound();

    /// <summary>成員看 @bot 的即時串流：確認成員資格，且這個 execution 屬於這個群組的 Bot 訊息（不能拿來讀別人的個人 execution）。</summary>
    private static async Task<IResult> StreamBotEventsAsync(
        Guid groupId,
        Guid executionId,
        long? lastEventId,
        HttpContext httpContext,
        ICurrentUser currentUser,
        IChatGroupService groups,
        ExecutionService executions,
        IExecutionEventBus bus,
        CancellationToken cancellationToken)
    {
        if (!await groups.IsMemberAsync(currentUser.UserId, groupId, cancellationToken)
            || !await groups.IsGroupExecutionAsync(groupId, executionId, cancellationToken))
        {
            return GroupNotFound();
        }

        var after = long.TryParse(httpContext.Request.Headers["Last-Event-ID"], NumberStyles.None, CultureInfo.InvariantCulture, out var headerId)
            ? headerId
            : lastEventId ?? 0;
        httpContext.Response.Headers["X-Accel-Buffering"] = "no";
        return TypedResults.ServerSentEvents(ReadEventsAsync(executionId, after, executions, bus, cancellationToken));
    }

    private static async IAsyncEnumerable<SseItem<string>> ReadEventsAsync(
        Guid executionId,
        long after,
        ExecutionService executions,
        IExecutionEventBus bus,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var subscription = bus.Subscribe(executionId);
        var last = after;
        foreach (var stored in await executions.GetEventsAfterAsync(executionId, after, cancellationToken))
        {
            yield return ToSseItem(stored);
            last = stored.Sequence;
            if (stored.IsTerminal)
            {
                yield break;
            }
        }

        await foreach (var stored in subscription.ReadAllAsync(cancellationToken))
        {
            if (stored.Sequence <= last)
            {
                continue;
            }

            yield return ToSseItem(stored);
            last = stored.Sequence;
            if (stored.IsTerminal)
            {
                yield break;
            }
        }
    }

    private static SseItem<string> ToSseItem(StoredExecutionEvent stored) =>
        new(stored.Data, stored.EventType) { EventId = stored.Sequence.ToString(CultureInfo.InvariantCulture) };

    private static IResult ToResult(ChatGroupResult result, bool created = false) => result.Outcome switch
    {
        ChatGroupOutcome.Ok when result.Group is null => TypedResults.NoContent(),
        ChatGroupOutcome.Ok when created => TypedResults.Created((string?)null, ChatGroupResponse.From(result.Group!)),
        ChatGroupOutcome.Ok => TypedResults.Ok(ChatGroupResponse.From(result.Group!)),
        ChatGroupOutcome.Forbidden => ApiProblem.Create(StatusCodes.Status403Forbidden, "CHAT_GROUP_OWNER_ONLY", "只有群組擁有者可以這樣做。"),
        ChatGroupOutcome.Invalid => ApiProblem.Create(StatusCodes.Status400BadRequest, "CHAT_GROUP_INVALID", result.Problem ?? "群組資料不正確。"),
        _ => GroupNotFound(),
    };

    private static IResult GroupNotFound() => ApiProblem.Create(StatusCodes.Status404NotFound, NotFoundCode, "找不到這個群組。");
}

public sealed record CreateChatGroupRequest(string? Name, IReadOnlyList<Guid>? MemberIds);

public sealed record RenameChatGroupRequest(string? Name);

public sealed record AddChatGroupMembersRequest(IReadOnlyList<Guid>? UserIds);

/// <param name="ClientRequestId">@bot 的冪等鍵（重送不重複執行）；一般訊息可省略。</param>
/// <param name="AttachmentIds">經 <c>/api/chat-groups/{id}/attachments</c> 上傳、要給 Agent 的檔案；只能隨 @bot 訊息送出。</param>
public sealed record SendGroupMessageRequest(string? Text, Guid? ClientRequestId = null, IReadOnlyList<Guid>? AttachmentIds = null);

/// <summary>群組訊息附上的檔名（給其他成員看到這次 @bot 附了哪些檔案；也會成為之後 @bot 的群組背景）。</summary>
internal static class GroupAttachmentNote
{
    public static string Append(string text, IReadOnlyList<string> fileNames, int maxLength)
    {
        if (fileNames.Count == 0)
        {
            return text;
        }

        var note = "\n📎 " + string.Join("、", fileNames);
        var room = maxLength - text.TrimEnd().Length;
        if (room <= 3)
        {
            return text;
        }

        return text.TrimEnd() + (note.Length <= room ? note : string.Concat(note.AsSpan(0, room - 1), "…"));
    }
}

public sealed record ChatGroupSummaryResponse(Guid Id, string Name, Guid OwnerId, ChatGroupRole Role, int MemberCount, int UnreadCount, DateTimeOffset? LastMessageAt, DateTimeOffset UpdatedAt)
{
    public static ChatGroupSummaryResponse From(ChatGroupSummary g) => new(g.Id, g.Name, g.OwnerId, g.Role, g.MemberCount, g.UnreadCount, g.LastMessageAt, g.UpdatedAt);
}

public sealed record ChatGroupMemberResponse(Guid UserId, string DisplayName, string? AccountName, string? Department, ChatGroupRole Role);

public sealed record ChatGroupResponse(Guid Id, string Name, Guid OwnerId, DateTimeOffset CreatedAt, IReadOnlyList<ChatGroupMemberResponse> Members)
{
    public static ChatGroupResponse From(ChatGroupDetail g) =>
        new(g.Id, g.Name, g.OwnerId, g.CreatedAt, [.. g.Members.Select(m => new ChatGroupMemberResponse(m.UserId, m.DisplayName, m.AccountName, m.Department, m.Role))]);
}

/// <param name="ExecutionId">Bot 訊息：對應的執行，Pending 時以群組的串流端點看即時回答。</param>
public sealed record GroupMessageResponse(
    Guid Id,
    Guid GroupId,
    GroupSenderKind SenderKind,
    Guid? SenderId,
    string? SenderName,
    string Body,
    DateTimeOffset CreatedAt,
    Guid? ExecutionId,
    Guid? InvokedById,
    string? InvokedByName,
    BotReplyStatus? BotStatus)
{
    public static GroupMessageResponse From(GroupMessageItem m) =>
        new(m.Id, m.GroupId, m.SenderKind, m.SenderId, m.SenderName, m.Body, m.CreatedAt, m.ExecutionId, m.InvokedById, m.InvokedByName, m.BotStatus);
}

public sealed record GroupMessagePageResponse(IReadOnlyList<GroupMessageResponse> Messages, bool HasMore);

/// <param name="Bot">@bot 已開始執行時的 Pending Bot 訊息。</param>
/// <param name="BotProblem">@bot 無法執行時的原因（訊息本身已送出）。</param>
public sealed record SendGroupMessageResponse(GroupMessageResponse Message, GroupMessageResponse? Bot, string? BotProblem);

public sealed record GroupReadResponse(Guid GroupId, Guid ReaderId, DateTimeOffset ReadAt);
