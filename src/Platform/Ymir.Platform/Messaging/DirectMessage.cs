namespace Ymir.Platform.Messaging;

/// <summary>
/// 使用者之間的一對一訊息（ADR-0023）。屬於共用核心（schema <c>platform</c>），不屬於任何子產品；
/// 一律以 (<see cref="SenderId"/>, <see cref="RecipientId"/>) 判斷誰能看，不信任前端傳入的身分（SA §12）。
/// </summary>
public sealed class DirectMessage
{
    /// <summary>單則訊息上限；聊天不是傳檔或長文的管道，太長的內容請改用對話附件或網站分享。</summary>
    public const int BodyMaxLength = 4000;

    private DirectMessage()
    {
    }

    public Guid Id { get; private set; }

    public Guid SenderId { get; private set; }

    public Guid RecipientId { get; private set; }

    public string Body { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>收件者讀取的時間；null 表示未讀。</summary>
    public DateTimeOffset? ReadAt { get; private set; }

    public static DirectMessage Create(Guid senderId, Guid recipientId, string body, DateTimeOffset now)
    {
        if (senderId == recipientId)
        {
            throw new ArgumentException("不能傳訊息給自己。", nameof(recipientId));
        }

        var text = NormalizeBody(body) ?? throw new ArgumentException("訊息不可空白或超過長度上限。", nameof(body));
        return new DirectMessage
        {
            // v7：依時間排序，分頁以 id 當游標即可。
            Id = Guid.CreateVersion7(now),
            SenderId = senderId,
            RecipientId = recipientId,
            Body = text,
            CreatedAt = now,
        };
    }

    /// <summary>去掉前後空白；空白或超過 <see cref="BodyMaxLength"/> 時回傳 null。</summary>
    public static string? NormalizeBody(string? body)
    {
        var text = body?.Trim();
        return string.IsNullOrEmpty(text) || text.Length > BodyMaxLength ? null : text;
    }

    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;
}
