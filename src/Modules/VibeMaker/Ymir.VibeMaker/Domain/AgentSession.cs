namespace Ymir.VibeMaker.Domain;

/// <summary>
/// 底層 Agent session（SA §8 AGENT_SESSION），一個 Conversation 一個。
/// Pi 以 <c>--session-id</c> 使用 <see cref="Id"/> 續接上下文，所以 <see cref="ProviderSessionId"/> 等於 Id（ADR-0003）。
/// </summary>
public sealed class AgentSession
{
    public const string PiProvider = "PI";

    private AgentSession()
    {
    }

    public Guid Id { get; private set; }

    public Guid ConversationId { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public Guid? RuntimeId { get; private set; }

    public string Provider { get; private set; } = PiProvider;

    public string? ProviderSessionId { get; private set; }

    public AgentSessionStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static AgentSession Create(Conversation conversation, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        var id = Guid.CreateVersion7(now);
        return new AgentSession
        {
            Id = id,
            ConversationId = conversation.Id,
            WorkspaceId = conversation.WorkspaceId,
            ProviderSessionId = id.ToString("D"),
            Status = AgentSessionStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void BindRuntime(Guid runtimeId, DateTimeOffset now)
    {
        RuntimeId = runtimeId;
        UpdatedAt = now;
    }
}

public enum AgentSessionStatus
{
    Active = 0,
    Error = 1,
    Closed = 2,
}
