namespace Ymir.Platform.Messaging;

/// <summary>
/// 群組聊天（ADR-0026 §1）。所有以使用者為參數的方法都只回傳該使用者所屬、未封存的群組；
/// 非成員、不存在、已封存一律當作找不到（null / NotFound），不透露群組是否存在。
/// </summary>
public interface IChatGroupService
{
    Task<IReadOnlyList<ChatGroupSummary>> ListAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>建立群組，建立者為擁有者；<paramref name="memberIds"/> 只接受未停用的其他使用者。</summary>
    Task<ChatGroupResult> CreateAsync(Guid ownerId, string name, IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken);

    Task<ChatGroupDetail?> GetAsync(Guid userId, Guid groupId, CancellationToken cancellationToken);

    Task<ChatGroupResult> RenameAsync(Guid userId, Guid groupId, string name, CancellationToken cancellationToken);

    Task<ChatGroupResult> AddMembersAsync(Guid userId, Guid groupId, IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken);

    /// <summary>擁有者移除成員，或成員移除自己（退出）。擁有者不能移除自己，只能封存。</summary>
    Task<ChatGroupResult> RemoveMemberAsync(Guid userId, Guid groupId, Guid memberId, CancellationToken cancellationToken);

    Task<ChatGroupResult> ArchiveAsync(Guid userId, Guid groupId, CancellationToken cancellationToken);

    Task<GroupMessagePage?> GetMessagesAsync(Guid userId, Guid groupId, Guid? before, int take, CancellationToken cancellationToken);

    /// <summary>寫入資料庫後才通知所有成員（先寫再發佈）。</summary>
    Task<GroupMessageSendResult> SendAsync(Guid userId, Guid groupId, string body, CancellationToken cancellationToken);

    Task<GroupRead?> MarkReadAsync(Guid userId, Guid groupId, CancellationToken cancellationToken);

    Task<bool> IsMemberAsync(Guid userId, Guid groupId, CancellationToken cancellationToken);

    // ---- @bot（ADR-0026 §2）：只由已確認成員資格的流程呼叫 ----

    /// <summary>最近的群組訊息（由舊到新，最多 <paramref name="take"/> 則），作為 Agent 的背景；不含 Pending 的 Bot 訊息。</summary>
    Task<IReadOnlyList<GroupMessageItem>> GetRecentAsync(Guid groupId, int take, CancellationToken cancellationToken);

    Task<GroupMessageItem> AddBotPendingAsync(Guid groupId, Guid invokedById, Guid executionId, CancellationToken cancellationToken);

    /// <summary>把 Pending 的 Bot 訊息更新為回答或失敗摘要並通知成員；已完成的不變（冪等）。</summary>
    Task CompleteBotAsync(Guid messageId, string body, bool failed, CancellationToken cancellationToken);

    /// <summary>所有仍在 Pending 的 Bot 訊息（背景工作補齊用）。</summary>
    Task<IReadOnlyList<PendingBotReply>> ListPendingBotRepliesAsync(CancellationToken cancellationToken);

    /// <summary>群組名稱（管理介面的執行環境清單：群組擁有的 runtime 以群組名稱顯示，ADR-0026 影響）。只由 Admin 流程呼叫。</summary>
    Task<IReadOnlyDictionary<Guid, string>> FindNamesAsync(IReadOnlyCollection<Guid> groupIds, CancellationToken cancellationToken);

    /// <summary>這個 execution 是否屬於這個群組的 Bot 訊息（串流端點的授權）。</summary>
    Task<bool> IsGroupExecutionAsync(Guid groupId, Guid executionId, CancellationToken cancellationToken);
}

public enum ChatGroupOutcome
{
    Ok,
    NotFound,
    /// <summary>是成員但沒有權限（只有擁有者可以管理）。</summary>
    Forbidden,
    Invalid,
}

public sealed record ChatGroupResult(ChatGroupOutcome Outcome, ChatGroupDetail? Group = null, string? Problem = null);

public sealed record ChatGroupSummary(Guid Id, string Name, Guid OwnerId, ChatGroupRole Role, int MemberCount, int UnreadCount, DateTimeOffset? LastMessageAt, DateTimeOffset UpdatedAt);

public sealed record ChatGroupMemberItem(Guid UserId, string DisplayName, string? AccountName, string? Department, ChatGroupRole Role);

public sealed record ChatGroupDetail(Guid Id, string Name, Guid OwnerId, DateTimeOffset CreatedAt, IReadOnlyList<ChatGroupMemberItem> Members);

/// <param name="SenderName">成員訊息為寄件者的顯示名稱；Bot 訊息為 null。</param>
/// <param name="InvokedByName">Bot 訊息：呼叫者的顯示名稱。</param>
public sealed record GroupMessageItem(
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
    BotReplyStatus? BotStatus);

public sealed record GroupMessagePage(IReadOnlyList<GroupMessageItem> Messages, bool HasMore);

public enum GroupMessageSendOutcome
{
    Sent,
    NotFound,
    InvalidBody,
}

public sealed record GroupMessageSendResult(GroupMessageSendOutcome Outcome, GroupMessageItem? Message);

public sealed record GroupRead(Guid GroupId, Guid ReaderId, DateTimeOffset ReadAt);

public sealed record PendingBotReply(Guid MessageId, Guid GroupId, Guid ExecutionId, DateTimeOffset CreatedAt);

/// <summary>群組有新訊息或 Bot 回覆更新；所有成員都會收到。</summary>
public sealed record GroupMessageNotification(GroupMessageItem Message) : DirectMessageNotification;

/// <summary>群組改名、成員異動、封存，或自己被移除（<paramref name="Removed"/>）。前端重新讀取群組清單與詳情。</summary>
public sealed record GroupUpdatedNotification(Guid GroupId, bool Removed) : DirectMessageNotification;

/// <summary>成員標為已讀（自己的其他分頁更新未讀數）。</summary>
public sealed record GroupReadNotification(GroupRead Read) : DirectMessageNotification;
