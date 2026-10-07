namespace Ymir.VibeMaker.Domain;

/// <summary>
/// 使用者附加在訊息上的檔案（圖片、影片、文件…）。檔案本身存在使用者 runtime 的工作目錄
/// （<see cref="Path"/> 是相對於工作目錄的路徑，例如 <c>uploads/0a1b2c3d-photo.png</c>），資料庫只保存中繼資料，
/// 與 Agent 產生的檔案一樣由 runtime 保存（ADR-0007）。
/// 先上傳（<see cref="MessageId"/> 為 null）、送出訊息時才綁定，同一個附件只能綁定一次。
/// </summary>
public sealed class MessageAttachment
{
    public const int FileNameMaxLength = 200;
    public const int PathMaxLength = 300;
    public const int ContentTypeMaxLength = 100;

    private MessageAttachment()
    {
    }

    public Guid Id { get; private set; }

    public Guid ConversationId { get; private set; }

    /// <summary>上傳者；跨模組只存 id（ADR-0001）。</summary>
    public Guid UserId { get; private set; }

    /// <summary>綁定的 USER 訊息；尚未送出時為 null。</summary>
    public Guid? MessageId { get; private set; }

    /// <summary>顯示用的原始檔名（已清理過不安全的字元）。</summary>
    public string FileName { get; private set; } = string.Empty;

    /// <summary>工作目錄內的相對路徑，由伺服器產生，不接受外部輸入。</summary>
    public string Path { get; private set; } = string.Empty;

    /// <summary>伺服器判斷的類型（圖片以檔頭判斷，不信任瀏覽器傳來的 Content-Type）。</summary>
    public string ContentType { get; private set; } = string.Empty;

    public long Size { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static MessageAttachment Create(Guid id, Guid conversationId, Guid userId, string fileName, string path, string contentType, long size, DateTimeOffset now) =>
        new()
        {
            Id = id,
            ConversationId = conversationId,
            UserId = userId,
            FileName = DomainGuard.RequiredText(fileName, FileNameMaxLength, nameof(fileName)),
            Path = DomainGuard.RequiredText(path, PathMaxLength, nameof(path)),
            ContentType = DomainGuard.RequiredText(contentType, ContentTypeMaxLength, nameof(contentType)),
            Size = size >= 0 ? size : throw new DomainValidationException("size must not be negative."),
            CreatedAt = now,
        };

    public void AttachTo(Guid messageId)
    {
        if (MessageId is not null)
        {
            throw new InvalidOperationException($"Attachment {Id} is already attached to message {MessageId}.");
        }

        MessageId = messageId;
    }
}
