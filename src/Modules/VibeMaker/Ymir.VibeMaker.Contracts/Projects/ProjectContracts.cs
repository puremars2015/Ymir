namespace Ymir.VibeMaker.Contracts.Projects;

public sealed record CreateProjectRequest(string Name);

/// <summary>專案：使用者 runtime 內的一個檔案群組（ADR-0007）。</summary>
/// <param name="SystemPrompt">專案專用的 system prompt；null 表示未設定。</param>
public sealed record ProjectResponse(Guid Id, string Name, string? SystemPrompt, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary><c>PATCH /api/projects/{id}</c>：只更新有給的欄位。</summary>
/// <param name="Name">新名稱；null 表示不變。</param>
/// <param name="SystemPrompt">新的 system prompt；null 表示不變，空字串表示清除。上限 10,000 字。</param>
public sealed record UpdateProjectRequest(string? Name, string? SystemPrompt);

/// <summary><c>GET /api/runtime</c>：目前使用者的執行環境（一個使用者一個，ADR-0007）。尚未建立時 <see cref="Status"/> 為 <c>NOT_CREATED</c>。</summary>
public sealed record RuntimeStatusResponse(string Status, string? Provider, string? ImageVersion, DateTimeOffset? LastActiveAt);
