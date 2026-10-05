namespace Ymir.VibeMaker.Contracts.Projects;

public sealed record CreateProjectRequest(string Name);

/// <summary>專案：使用者 runtime 內的一個檔案群組（ADR-0007）。</summary>
public sealed record ProjectResponse(Guid Id, string Name, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary><c>GET /api/runtime</c>：目前使用者的執行環境（一個使用者一個，ADR-0007）。尚未建立時 <see cref="Status"/> 為 <c>NOT_CREATED</c>。</summary>
public sealed record RuntimeStatusResponse(string Status, string? Provider, string? ImageVersion, DateTimeOffset? LastActiveAt);
