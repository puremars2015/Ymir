namespace Ymir.VibeMaker.Contracts.Workspaces;

public sealed record CreateWorkspaceRequest(string Name);

public sealed record WorkspaceResponse(Guid Id, string Name, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary><c>GET /api/workspaces/{id}/runtime</c>（SA §9）。尚未建立 runtime 時 <see cref="Status"/> 為 <c>NOT_CREATED</c>。</summary>
public sealed record RuntimeStatusResponse(Guid WorkspaceId, string Status, string? Provider, string? ImageVersion, DateTimeOffset? LastActiveAt);
