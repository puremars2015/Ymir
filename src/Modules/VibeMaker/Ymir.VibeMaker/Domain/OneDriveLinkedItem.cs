namespace Ymir.VibeMaker.Domain;

public enum OneDriveLinkedItemState
{
    Pending,
    Downloaded,
    Failed,
    CloudDeleted,
}

/// <summary>使用者明確選取的雲端項目；不同於 ADR-0013 的同步根目錄（ADR-0022）。</summary>
public sealed class OneDriveLinkedItem
{
    private OneDriveLinkedItem() { }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid ScopeId { get; private set; }
    public string DriveId { get; private set; } = string.Empty;
    public string ItemId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string LocalName { get; private set; } = string.Empty;
    public bool IsFolder { get; private set; }
    public bool ReadOnly { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastDownloadedAt { get; private set; }
    public OneDriveLinkedItemState State { get; private set; }
    public string? LastError { get; private set; }
    public int ConflictCount { get; private set; }

    public static OneDriveLinkedItem Create(Guid id, Guid userId, Guid scopeId, string driveId, string itemId, string name, string localName, bool isFolder, bool readOnly, DateTimeOffset now) => new()
    {
        Id = id,
        UserId = userId,
        ScopeId = scopeId,
        DriveId = DomainGuard.RequiredText(driveId, OneDriveConnection.DriveIdMaxLength, nameof(driveId)),
        ItemId = DomainGuard.RequiredText(itemId, OneDriveConnection.ItemIdMaxLength, nameof(itemId)),
        Name = DomainGuard.RequiredText(name, 255, nameof(name)),
        LocalName = DomainGuard.RequiredText(localName, 200, nameof(localName)),
        IsFolder = isFolder,
        ReadOnly = readOnly,
        CreatedAt = now,
        State = OneDriveLinkedItemState.Pending,
    };

    public void SetReadOnly(bool readOnly) => ReadOnly = readOnly;

    public void Downloaded(int conflicts, string? warning, DateTimeOffset now)
    {
        State = OneDriveLinkedItemState.Downloaded;
        LastDownloadedAt = now;
        LastError = warning;
        ConflictCount = conflicts;
    }

    public void Fail(bool cloudDeleted) => (State, LastError) = cloudDeleted
        ? (OneDriveLinkedItemState.CloudDeleted, "雲端項目已刪除或移出授權範圍，本機副本保留。")
        : (OneDriveLinkedItemState.Failed, "OneDrive 連結檔案未更新，使用本機檔案。請檢查連線或重新授權。");
}

/// <summary>獨立於原同步資料夾的基準；雲端 eTag 與本機 SHA-256 用於保護未寫回的修改。</summary>
public sealed class OneDriveLinkedFile
{
    private OneDriveLinkedFile() { }

    public const int PathMaxLength = 1024;

    public Guid Id { get; private set; }
    public Guid LinkId { get; private set; }
    public string Path { get; private set; } = string.Empty;
    public string PathKey { get; private set; } = string.Empty;
    public string ItemId { get; private set; } = string.Empty;
    public string? ETag { get; private set; }
    public string LocalHash { get; private set; } = string.Empty;

    public static OneDriveLinkedFile Create(Guid linkId, string path) => new()
    {
        Id = Guid.NewGuid(),
        LinkId = linkId,
        Path = path,
        PathKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path))),
    };

    public void Update(string itemId, string? eTag, string hash) => (ItemId, ETag, LocalHash) = (itemId, eTag, hash);
}
