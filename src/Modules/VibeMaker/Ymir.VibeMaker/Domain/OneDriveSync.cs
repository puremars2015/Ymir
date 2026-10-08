namespace Ymir.VibeMaker.Domain;

public enum OneDriveSyncState
{
    /// <summary>最後一次同步成功。</summary>
    Synced,

    /// <summary>等待背景同步（執行結束後、手動重試、失敗後自動重試）。</summary>
    Pending,

    /// <summary>同步失敗且不再自動重試（重試次數用完、授權失效）；使用者可以手動重試。</summary>
    Failed,
}

/// <summary>
/// 一個工作目錄（專案或未分組對話）在 OneDrive 的對應資料夾與同步狀態（ADR-0013 §3、§4）。
/// <see cref="ScopeId"/> 是專案 id 或對話 id（工作目錄由它推導，ADR-0007）。<see cref="UploadPending"/> 是持久化的同步工作：
/// 服務重啟後背景 worker 會繼續處理。
/// </summary>
public sealed class OneDriveSyncScope
{
    public const int MaxAttempts = 5;
    public const int FolderNameMaxLength = 200;

    private OneDriveSyncScope()
    {
    }

    public Guid ScopeId { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>觸發背景同步的對話（專案內任何一個對話皆可；用來推導工作目錄）。</summary>
    public Guid ConversationId { get; private set; }

    /// <summary>建立資料夾時的 drive 與根資料夾；使用者換了 drive 或根資料夾時重新建立對應。</summary>
    public string DriveId { get; private set; } = string.Empty;

    public string RootItemId { get; private set; } = string.Empty;

    public string FolderItemId { get; private set; } = string.Empty;

    /// <summary>雲端資料夾的相對位置（例如 <c>projects/報告-1a2b3c4d</c>），只用來顯示。</summary>
    public string FolderPath { get; private set; } = string.Empty;

    public OneDriveSyncState State { get; private set; }

    public bool UploadPending { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset? NextAttemptAt { get; private set; }

    public DateTimeOffset? LastSyncedAt { get; private set; }

    /// <summary>最後一次同步產生的衝突副本數。</summary>
    public int ConflictCount { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static OneDriveSyncScope Create(Guid scopeId, Guid userId, Guid conversationId, DateTimeOffset now) =>
        new() { ScopeId = scopeId, UserId = userId, ConversationId = conversationId, State = OneDriveSyncState.Synced, UpdatedAt = now };

    public void BindFolder(string driveId, string rootItemId, string folderItemId, string folderPath, DateTimeOffset now)
    {
        DriveId = DomainGuard.RequiredText(driveId, OneDriveConnection.DriveIdMaxLength, nameof(driveId));
        RootItemId = DomainGuard.RequiredText(rootItemId, OneDriveConnection.ItemIdMaxLength, nameof(rootItemId));
        FolderItemId = DomainGuard.RequiredText(folderItemId, OneDriveConnection.ItemIdMaxLength, nameof(folderItemId));
        FolderPath = DomainGuard.RequiredText(folderPath, FolderNameMaxLength, nameof(folderPath));
        UpdatedAt = now;
    }

    /// <summary>
    /// 新的一輪同步開始（執行前下載完成、或手動同步）：衝突數從這一輪重新累計，
    /// 執行結束後的背景同步再加上它自己的衝突數。
    /// </summary>
    public void StartRound(int conflicts, DateTimeOffset now)
    {
        ConflictCount = conflicts;
        if (State == OneDriveSyncState.Failed && !UploadPending)
        {
            State = OneDriveSyncState.Synced;
            LastError = null;
        }

        UpdatedAt = now;
    }

    /// <summary>排入背景同步（立即處理）；手動重試也會重設重試次數。</summary>
    public void Enqueue(Guid conversationId, DateTimeOffset now)
    {
        ConversationId = conversationId;
        UploadPending = true;
        Attempts = 0;
        NextAttemptAt = now;
        State = OneDriveSyncState.Pending;
        UpdatedAt = now;
    }

    /// <param name="message">給使用者看的摘要（部分檔案失敗或被略過）；<paramref name="failed"/> 為 true 時狀態是失敗，可手動重試。</param>
    public void Succeeded(int conflicts, string? message, bool failed, DateTimeOffset now)
    {
        UploadPending = false;
        Attempts = 0;
        NextAttemptAt = null;
        LastSyncedAt = now;
        ConflictCount += conflicts;
        State = failed ? OneDriveSyncState.Failed : OneDriveSyncState.Synced;
        LastError = Truncate(message);
        UpdatedAt = now;
    }

    /// <summary>背景同步失敗：在次數內以退避時間重試，用完或 <paramref name="retry"/> 為 false 時停止。</summary>
    public void AttemptFailed(string error, bool retry, DateTimeOffset now)
    {
        Attempts++;
        LastError = Truncate(error);
        UpdatedAt = now;
        if (retry && UploadPending && Attempts < MaxAttempts)
        {
            State = OneDriveSyncState.Pending;
            NextAttemptAt = now + RetryDelay(Attempts);
            return;
        }

        UploadPending = false;
        NextAttemptAt = null;
        State = OneDriveSyncState.Failed;
    }

    /// <summary>執行前下載失敗：只記錄原因（執行結束後的同步工作會再試一次）。</summary>
    public void DownloadFailed(string error, DateTimeOffset now)
    {
        LastError = Truncate(error);
        State = OneDriveSyncState.Failed;
        UpdatedAt = now;
    }

    /// <summary>1、5、15、60 分鐘。</summary>
    public static TimeSpan RetryDelay(int attempts) => attempts switch
    {
        <= 1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(5),
        3 => TimeSpan.FromMinutes(15),
        _ => TimeSpan.FromMinutes(60),
    };

    private static string? Truncate(string? error) =>
        error is { Length: > OneDriveConnection.ErrorMaxLength } ? error[..OneDriveConnection.ErrorMaxLength] : error;
}

/// <summary>
/// 已同步的檔案：雲端 item id 與 eTag、同步當時本機的大小與修改時間（ADR-0013 §4）。
/// eTag 不同表示雲端改過；本機大小或修改時間不同表示本機改過；兩邊都改過就是衝突。
/// </summary>
public sealed class OneDriveSyncItem
{
    private OneDriveSyncItem()
    {
    }

    public Guid Id { get; private set; }

    public Guid ScopeId { get; private set; }

    public string Path { get; private set; } = string.Empty;

    public string ItemId { get; private set; } = string.Empty;

    public string? ETag { get; private set; }

    /// <summary>同步當時本機檔案的大小與修改時間；null 表示還沒有對應的本機版本（下一次上傳時視為已修改）。</summary>
    public long? LocalSize { get; private set; }

    public DateTimeOffset? LocalModifiedAt { get; private set; }

    public DateTimeOffset SyncedAt { get; private set; }

    public const int PathMaxLength = 1024;

    /// <summary>路徑原樣保存（不 trim）：必須與工作目錄內的檔名完全一致。</summary>
    public static OneDriveSyncItem Create(Guid scopeId, string path, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(path) || path.Length > PathMaxLength)
        {
            throw new DomainValidationException("path is invalid.");
        }

        return new() { Id = Guid.NewGuid(), ScopeId = scopeId, Path = path, SyncedAt = now };
    }

    public void Update(string itemId, string? eTag, long? localSize, DateTimeOffset? localModifiedAt, DateTimeOffset now)
    {
        ItemId = DomainGuard.RequiredText(itemId, OneDriveConnection.ItemIdMaxLength, nameof(itemId));
        ETag = eTag;
        LocalSize = localSize;
        LocalModifiedAt = localModifiedAt;
        SyncedAt = now;
    }

    public void SetLocal(long size, DateTimeOffset modifiedAt)
    {
        LocalSize = size;
        LocalModifiedAt = modifiedAt;
    }

    public bool MatchesLocal(long size, DateTimeOffset modifiedAt) => LocalSize == size && LocalModifiedAt == modifiedAt;
}
