namespace Ymir.VibeMaker.Domain;

/// <summary>
/// 對話（SA §8 CONVERSATION）。是 UI / 業務物件，與底層 <see cref="AgentSession"/> 分開（SA §5）。
/// 可以屬於某個 <see cref="Project"/>，也可以不分組（ADR-0007）。
/// </summary>
public sealed class Conversation
{
    public const int TitleMaxLength = 300;

    public const int ModelIdMaxLength = 200;

    private Conversation()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>所屬專案；null 表示未分組，Agent 在對話自己的目錄工作（ADR-0007）。</summary>
    public Guid? ProjectId { get; private set; }

    public Guid UserId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    /// <summary>最後選用的模型（LiteLLM model_name）；null 表示使用預設模型。</summary>
    public string? ModelId { get; private set; }

    public ConversationStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Conversation Create(Guid userId, Project? project, string title, DateTimeOffset now)
    {
        if (project is not null && project.UserId != userId)
        {
            throw new DomainValidationException("project belongs to another user.");
        }

        return new Conversation
        {
            Id = Guid.CreateVersion7(now),
            ProjectId = project?.Id,
            UserId = userId,
            Title = DomainGuard.RequiredText(title, TitleMaxLength, nameof(title)),
            Status = ConversationStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Touch(DateTimeOffset now) => UpdatedAt = now;

    /// <summary>記住使用者選的模型，下次開啟對話時沿用（像 ChatGPT）。模型是否可用由 Application 層檢查。</summary>
    public void SelectModel(string modelId) => ModelId = DomainGuard.RequiredText(modelId, ModelIdMaxLength, nameof(modelId));
}

public enum ConversationStatus
{
    Active = 0,
    Archived = 1,
}
