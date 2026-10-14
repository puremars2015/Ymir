namespace Ymir.Platform.Messaging;

/// <summary>使用者之間的訊息（ADR-0023）。所有方法都以「目前使用者」為一方，呼叫端必須傳入 <c>ICurrentUser.UserId</c>。</summary>
public interface IDirectMessageService
{
    /// <summary>右側聯絡人名單：未停用的其他使用者，附未讀數與最後訊息時間（未讀優先、再依最近聯絡排序）。</summary>
    Task<IReadOnlyList<DirectContact>> ListContactsAsync(Guid userId, string? search, CancellationToken cancellationToken);

    /// <summary>
    /// 與 <paramref name="peerId"/> 的訊息，由新到舊取 <paramref name="take"/> 筆（<paramref name="before"/> 為游標），回傳時依時間由舊到新排列。
    /// 對方不存在時回傳 null。
    /// </summary>
    Task<DirectMessagePage?> GetHistoryAsync(Guid userId, Guid peerId, Guid? before, int take, CancellationToken cancellationToken);

    /// <summary>寫入資料庫後才通知雙方的即時連線（與 execution 事件相同：先寫再發佈）。</summary>
    Task<DirectMessageSendResult> SendAsync(Guid senderId, Guid recipientId, string body, CancellationToken cancellationToken);

    /// <summary>把對方傳給自己的未讀訊息標為已讀，並通知雙方（自己的其他分頁更新未讀數、對方看到已讀）。對方不存在時回傳 null。</summary>
    Task<DirectMessagesRead?> MarkReadAsync(Guid userId, Guid peerId, CancellationToken cancellationToken);
}

/// <summary>訊息的唯讀快照（給 API 與即時通知用，不外流可變的實體）。</summary>
public sealed record DirectMessageItem(Guid Id, Guid SenderId, Guid RecipientId, string Body, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt)
{
    public static DirectMessageItem From(DirectMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new(message.Id, message.SenderId, message.RecipientId, message.Body, message.CreatedAt, message.ReadAt);
    }
}

/// <param name="HasMore">是否還有更舊的訊息。</param>
public sealed record DirectMessagePage(IReadOnlyList<DirectMessageItem> Messages, bool HasMore);

/// <summary>聯絡人名單的一列；只放顯示用的資料，不含 email 等個資（與 <c>/api/users/search</c> 相同原則）。</summary>
public sealed record DirectContact(Guid UserId, string DisplayName, string? AccountName, string? Department, int UnreadCount, DateTimeOffset? LastMessageAt);

public enum DirectMessageSendOutcome
{
    Sent,
    /// <summary>收件者不存在、已停用或是自己：一律當作找不到，不透露帳號狀態。</summary>
    RecipientNotFound,
    InvalidBody,
}

public sealed record DirectMessageSendResult(DirectMessageSendOutcome Outcome, DirectMessageItem? Message);

/// <param name="ReaderId">讀取的人。</param>
/// <param name="PeerId">訊息的寄件者（被讀的那一方）。</param>
/// <param name="Count">這次標為已讀的則數。</param>
public sealed record DirectMessagesRead(Guid ReaderId, Guid PeerId, DateTimeOffset ReadAt, int Count);
