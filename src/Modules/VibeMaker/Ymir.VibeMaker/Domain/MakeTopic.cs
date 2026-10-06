namespace Ymir.VibeMaker.Domain;

/// <summary>
/// <c>/make</c> 的主題（例如「小工具架設」「網站系統架設」），由 Admin 在網頁上管理。
/// <see cref="Instructions"/> 是給 Agent 的建置指示，只在後端組合 prompt 時使用，不回傳給一般使用者。
/// </summary>
public sealed class MakeTopic
{
    public const int NameMaxLength = 50;
    public const int DescriptionMaxLength = 200;
    public const int InstructionsMaxLength = 4_000;

    private MakeTopic()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>顯示在主題按鈕上的一句說明。</summary>
    public string? Description { get; private set; }

    public string Instructions { get; private set; } = string.Empty;

    public int SortOrder { get; private set; }

    public bool IsEnabled { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static MakeTopic Create(string name, string? description, string instructions, int sortOrder, bool isEnabled, DateTimeOffset now)
    {
        var topic = new MakeTopic { Id = Guid.CreateVersion7(now), CreatedAt = now };
        topic.Update(name, description, instructions, sortOrder, isEnabled, now);
        return topic;
    }

    public void Update(string name, string? description, string instructions, int sortOrder, bool isEnabled, DateTimeOffset now)
    {
        Name = DomainGuard.RequiredText(name, NameMaxLength, nameof(name));
        Description = DomainGuard.OptionalText(description, DescriptionMaxLength, nameof(description));
        Instructions = DomainGuard.RequiredText(instructions, InstructionsMaxLength, nameof(instructions));
        SortOrder = sortOrder;
        IsEnabled = isEnabled;
        UpdatedAt = now;
    }
}
