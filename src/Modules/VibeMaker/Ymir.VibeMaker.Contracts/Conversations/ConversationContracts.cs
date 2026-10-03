namespace Ymir.VibeMaker.Contracts.Conversations;

public sealed record CreateConversationRequest(Guid WorkspaceId, string Title);

public sealed record ConversationResponse(Guid Id, Guid WorkspaceId, string Title, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <param name="Role">USER / ASSISTANT / SYSTEM / TOOL（SA §8）。</param>
/// <param name="MessageType">TEXT / STATUS / TOOL_EVENT / ERROR（SA §8）。</param>
public sealed record MessageResponse(Guid Id, string Role, string MessageType, string Content, long SequenceNo, Guid? ExecutionId, DateTimeOffset CreatedAt);
