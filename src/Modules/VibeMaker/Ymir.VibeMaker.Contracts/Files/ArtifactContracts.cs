namespace Ymir.VibeMaker.Contracts.Files;

public sealed record ArtifactGroupResponse(Guid ExecutionId, Guid ConversationId, Guid? MessageId, DateTimeOffset CreatedAt, List<WorkspaceFileResponse> Files);
