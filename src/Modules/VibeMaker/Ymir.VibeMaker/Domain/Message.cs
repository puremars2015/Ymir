namespace Ymir.VibeMaker.Domain;

/// <summary>對話訊息（SA §8 MESSAGE），是 UI 顯示的真實來源（ADR-0003）。</summary>
public sealed class Message
{
    public const int ContentMaxLength = 32_000;

    private Message()
    {
    }

    public Guid Id { get; private set; }

    public Guid ConversationId { get; private set; }

    public MessageRole Role { get; private set; }

    public string Content { get; private set; } = string.Empty;

    public MessageType MessageType { get; private set; }

    /// <summary>對話內的順序（SA §8）。</summary>
    public long SequenceNo { get; private set; }

    /// <summary>產生此訊息的 execution（開發規劃 §9 補強）。</summary>
    public Guid? ExecutionId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Message CreateUser(Guid conversationId, string content, long sequenceNo, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            ConversationId = conversationId,
            Role = MessageRole.User,
            Content = DomainGuard.RequiredText(content, ContentMaxLength, nameof(content)),
            MessageType = MessageType.Text,
            SequenceNo = sequenceNo,
            CreatedAt = now,
        };

    public static Message CreateAssistant(Guid conversationId, Guid executionId, string content, MessageType type, long sequenceNo, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            ConversationId = conversationId,
            Role = MessageRole.Assistant,
            Content = content ?? string.Empty,
            MessageType = type,
            SequenceNo = sequenceNo,
            ExecutionId = executionId,
            CreatedAt = now,
        };

    internal void AttachExecution(Guid executionId) => ExecutionId = executionId;
}

public enum MessageRole
{
    User = 0,
    Assistant = 1,
    System = 2,
    Tool = 3,
}

public enum MessageType
{
    Text = 0,
    Status = 1,
    ToolEvent = 2,
    Error = 3,
}
