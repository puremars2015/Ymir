namespace Ymir.VibeMaker.Domain;

/// <summary>對話（SA §8 CONVERSATION）。是 UI / 業務物件，與底層 <see cref="AgentSession"/> 分開（SA §5）。</summary>
public sealed class Conversation
{
    public const int TitleMaxLength = 300;

    private Conversation()
    {
    }

    public Guid Id { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public Guid UserId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public ConversationStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Conversation Create(Workspace workspace, string title, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return new Conversation
        {
            Id = Guid.CreateVersion7(now),
            WorkspaceId = workspace.Id,
            UserId = workspace.UserId,
            Title = DomainGuard.RequiredText(title, TitleMaxLength, nameof(title)),
            Status = ConversationStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Touch(DateTimeOffset now) => UpdatedAt = now;
}

public enum ConversationStatus
{
    Active = 0,
    Archived = 1,
}
