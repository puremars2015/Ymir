namespace Ymir.VibeMaker.Contracts.Conversations;

/// <param name="ProjectId">所屬專案；省略表示未分組的對話（ADR-0007）。</param>
public sealed record CreateConversationRequest(Guid? ProjectId, string Title);

/// <param name="ProjectId">所屬專案；null 表示未分組（ADR-0007）。</param>
/// <param name="ModelId">最後選用的模型；null 表示預設模型。</param>
public sealed record ConversationResponse(Guid Id, Guid? ProjectId, string Title, string? ModelId, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <param name="Role">USER / ASSISTANT / SYSTEM / TOOL（SA §8）。</param>
/// <param name="MessageType">TEXT / STATUS / TOOL_EVENT / ERROR（SA §8）。</param>
public sealed record MessageResponse(Guid Id, string Role, string MessageType, string Content, long SequenceNo, Guid? ExecutionId, DateTimeOffset CreatedAt);
