namespace Ymir.Platform.Notifications;

/// <summary>通知種類（ADR-0032 §1）。N1 只有 execution 結束；排程、提醒與 Agent 訊息在後續階段加入。</summary>
public enum NotificationKind
{
    ExecutionCompleted,
    ExecutionFailed,
}

/// <summary>
/// 給某一位使用者的站內通知（ADR-0032 §1，共用核心 schema <c>platform</c>）。只有「通知某一位使用者」，沒有廣播。
/// 內容只是純文字摘要（SA §10、§12）；<see cref="Link"/> 只由伺服器依種類與 id 產生，只能是站內相對路徑。
/// </summary>
public sealed class Notification
{
    public const int TitleMaxLength = 80;
    public const int BodyMaxLength = 500;
    public const int LinkMaxLength = 200;

    private Notification()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public NotificationKind Kind { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Body { get; private set; }

    /// <summary>站內相對路徑（例如 <c>/c/{id}</c>）；null 表示不連到任何頁面。</summary>
    public string? Link { get; private set; }

    /// <summary>來源物件的 id（例如 execution id），只用於追溯，不給前端組網址。</summary>
    public Guid? SourceId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>null 表示未讀。</summary>
    public DateTimeOffset? ReadAt { get; private set; }

    public static Notification Create(Guid userId, NotificationKind kind, string title, string? body, string? link, Guid? sourceId, DateTimeOffset now)
    {
        if (link is not null && !IsSafeLink(link))
        {
            throw new ArgumentException("Notification links must be site-relative paths.", nameof(link));
        }

        return new Notification
        {
            // v7：依時間排序，分頁以 id 當游標即可。
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            Kind = kind,
            Title = Truncate(string.IsNullOrWhiteSpace(title) ? "Ymir 通知" : title.Trim(), TitleMaxLength),
            Body = string.IsNullOrWhiteSpace(body) ? null : Truncate(body.Trim(), BodyMaxLength),
            Link = link,
            SourceId = sourceId,
            CreatedAt = now,
        };
    }

    /// <summary>只接受站內相對路徑：以單一 <c>/</c> 開頭、不是 <c>//</c> 或 <c>/\</c>（瀏覽器會當成其他網域），沒有控制字元。</summary>
    public static bool IsSafeLink(string link) =>
        link.Length is > 0 and <= LinkMaxLength
        && link[0] == '/'
        && (link.Length == 1 || link[1] is not ('/' or '\\'))
        && !link.Any(char.IsControl);

    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
