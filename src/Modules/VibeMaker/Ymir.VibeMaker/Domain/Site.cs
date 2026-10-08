namespace Ymir.VibeMaker.Domain;

public enum SiteAccessMode
{
    /// <summary>任何人，不需要登入。</summary>
    Public,

    /// <summary>已登入、帳號未停用的 Ymir 使用者。</summary>
    AllUsers,

    /// <summary>擁有者與分享名單中的使用者。</summary>
    SelectedUsers,
}

public enum SiteStatus
{
    /// <summary>尚未有成功的版本，或已取消發布：SiteHost 回 404。</summary>
    Unpublished,

    Published,
}

/// <summary>
/// 發布的網站（ADR-0016）。網址是 <c>{Slug}.{BaseDomain}</c>；Slug 由平台隨機產生。
/// 內容是 <see cref="CurrentVersionId"/> 指向的版本；新版本完整寫好才切換。跨模組只存 user id（ADR-0001）。
/// Ymir 管理員與擁有者一律可以看（ADR-0016 §3）。
/// </summary>
public sealed class Site
{
    public const int SlugLength = 8;
    public const int NameMaxLength = 100;
    public const int SourcePathMaxLength = 1024;

    private Site()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Slug { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    /// <summary>來源對話（工作目錄由它推導，ADR-0007）。</summary>
    public Guid ConversationId { get; private set; }

    /// <summary>工作目錄內的來源目錄（相對路徑，<c>.</c> 表示工作目錄本身）。</summary>
    public string SourcePath { get; private set; } = ".";

    /// <summary>單頁應用：沒有副檔名的路徑回 <c>index.html</c>。</summary>
    public bool SpaMode { get; private set; }

    public SiteAccessMode AccessMode { get; private set; }

    public SiteStatus Status { get; private set; }

    public Guid? CurrentVersionId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public static Site Create(Guid id, Guid userId, string slug, string name, Guid conversationId, string sourcePath, bool spaMode, DateTimeOffset now)
    {
        var site = new Site
        {
            Id = id,
            UserId = userId,
            Slug = slug.Length == SlugLength ? slug : throw new DomainValidationException("slug is invalid."),
            AccessMode = SiteAccessMode.Public,
            Status = SiteStatus.Unpublished,
            CreatedAt = now,
        };
        site.Configure(name, conversationId, sourcePath, spaMode, now);
        return site;
    }

    public void Configure(string name, Guid conversationId, string sourcePath, bool spaMode, DateTimeOffset now)
    {
        Name = DomainGuard.RequiredText(name, NameMaxLength, nameof(name));
        ConversationId = conversationId;
        SourcePath = DomainGuard.RequiredText(sourcePath, SourcePathMaxLength, nameof(sourcePath));
        SpaMode = spaMode;
        UpdatedAt = now;
    }

    /// <summary>切換到已完整寫好的版本（ADR-0016 §2）。</summary>
    public void Publish(Guid versionId, DateTimeOffset now)
    {
        CurrentVersionId = versionId;
        Status = SiteStatus.Published;
        PublishedAt = now;
        UpdatedAt = now;
    }

    public void Unpublish(DateTimeOffset now)
    {
        Status = SiteStatus.Unpublished;
        UpdatedAt = now;
    }

    public void SetAccessMode(SiteAccessMode mode, DateTimeOffset now)
    {
        AccessMode = mode;
        UpdatedAt = now;
    }
}

public enum SiteVersionStatus
{
    Ready,
    Failed,

    /// <summary>超過保留數量，檔案已刪除。</summary>
    Pruned,
}

/// <summary>網站的一個發布版本（ADR-0016 §2）：檔案在網站 volume 的 <c>{siteId}/{versionId}/</c>。</summary>
public sealed class SiteVersion
{
    public const int ErrorMaxLength = 300;

    private SiteVersion()
    {
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    public string SourcePath { get; private set; } = ".";

    public int FileCount { get; private set; }

    public long TotalBytes { get; private set; }

    public SiteVersionStatus Status { get; private set; }

    public string? Error { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static SiteVersion Ready(Guid id, Guid siteId, string sourcePath, int fileCount, long totalBytes, DateTimeOffset now) =>
        new() { Id = id, SiteId = siteId, SourcePath = sourcePath, FileCount = fileCount, TotalBytes = totalBytes, Status = SiteVersionStatus.Ready, CreatedAt = now };

    public static SiteVersion Failed(Guid id, Guid siteId, string sourcePath, string error, DateTimeOffset now) =>
        new()
        {
            Id = id,
            SiteId = siteId,
            SourcePath = sourcePath,
            Status = SiteVersionStatus.Failed,
            Error = error.Length > ErrorMaxLength ? error[..ErrorMaxLength] : error,
            CreatedAt = now,
        };

    public void Prune() => Status = SiteVersionStatus.Pruned;
}

/// <summary>網站分享給的使用者（ADR-0016 §3，<see cref="SiteAccessMode.SelectedUsers"/>）；只授予瀏覽權。</summary>
public sealed class SiteShare
{
    private SiteShare()
    {
    }

    public Guid SiteId { get; private set; }

    public Guid UserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static SiteShare Create(Guid siteId, Guid userId, DateTimeOffset now) => new() { SiteId = siteId, UserId = userId, CreatedAt = now };
}

/// <summary>
/// 私人網站的登入票據（ADR-0016 §4）：60 秒、只能用一次、綁定網站與使用者；資料庫只存 SHA-256 雜湊。
/// SiteHost 兌換時以條件更新原子性地標記已使用。
/// </summary>
public sealed class SiteTicket
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    private SiteTicket()
    {
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public static SiteTicket Create(Guid siteId, Guid userId, string tokenHash, DateTimeOffset now) =>
        new() { Id = Guid.CreateVersion7(), SiteId = siteId, UserId = userId, TokenHash = tokenHash, ExpiresAt = now + Lifetime };
}
