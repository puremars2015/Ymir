namespace Ymir.Maintenance;

// 維護服務回傳的資料（ADR-0025 §1）。只含摘要：不含 host 路徑、掛載細節、環境變數、憑證或完整 CLI 輸出。
// API 直接以這些型別回應管理介面，所以欄位就是瀏覽器看得到的全部內容。

/// <summary>資料狀態：部分 store 查詢失敗時不可顯示為零，更不可視為沒有引用（ADR-0025 §1）。</summary>
public enum DataStatus
{
    Available,
    Stale,
    Failed,
    Unsupported,
}

/// <summary>自動回收目前的狀態。</summary>
public enum ReclaimState
{
    /// <summary>政策關閉自動回收。</summary>
    Disabled,

    /// <summary>政策開啟且受控鎖可用，自動回收運作中。</summary>
    Active,

    /// <summary>政策開啟，但與 runtime host / 部署共用的鎖無法使用：只提供監控與 dry run（ADR-0025 §4）。</summary>
    LocksUnavailable,
}

/// <summary>保護（不回收）的原因；null 代表可回收。</summary>
public enum ProtectionReason
{
    /// <summary>執行中、暫停中、建立中或正在啟動的 container。</summary>
    Active,

    /// <summary>不是可重建的 Agent runtime container（例如 API、入口、資料庫）。</summary>
    ServiceContainer,

    /// <summary>沒有回收允許標記（label）。</summary>
    NotReclaimable,

    /// <summary>部署設定明確排除。</summary>
    Excluded,

    /// <summary>workspace 不是外置掛載或目前無法存取。</summary>
    WorkspaceUnverified,

    /// <summary>有 container（包含已停止的）使用中的 image。</summary>
    Referenced,

    /// <summary>正式設定指向的 image（API Quadlet、runtime host 設定）。</summary>
    Pinned,

    /// <summary>repository 最近的版本，保留供回復。</summary>
    RecentVersion,

    /// <summary>部署設定明確保護的回復版本。</summary>
    ProtectedVersion,

    /// <summary>有其他非 Ymir 的 tag。</summary>
    ForeignTag,

    /// <summary>無法證明是 Ymir 的物件（沒有管理 label 也沒有登記）。</summary>
    OwnershipUnproven,

    /// <summary>無法讀取設定、引用或狀態：fail closed。</summary>
    StateUnknown,
}

/// <param name="CpuFreePercent">整台主機的 CPU 剩餘比例（兩次採樣之間）；第一次採樣前為 null。</param>
public sealed record HostResources(
    DataStatus Status,
    DateTimeOffset? SampledAt,
    int CpuCount,
    double? CpuUsedPercent,
    double? CpuFreePercent,
    long MemoryTotalBytes,
    long MemoryAvailableBytes,
    double MemoryAvailablePercent);

/// <summary>Podman store 所在的檔案系統（同一個檔案系統只列一次）。不回傳掛載路徑，只以代號與所屬 store 識別。</summary>
public sealed record FilesystemResources(
    string Id,
    IReadOnlyList<string> Stores,
    long TotalBytes,
    long AvailableBytes,
    double AvailablePercent);

public sealed record StoreStatus(string Name, DataStatus Status, DateTimeOffset? SampledAt, string? FilesystemId, string? Message);

/// <param name="UnusedSince">持續停止的起算時間（維護服務首次觀測到停止的時間）；使用中為 null。</param>
/// <param name="RetentionEligible">已超過保留天數、可在一般回收時刪除。</param>
public sealed record ContainerResource(
    string Store,
    string Name,
    string ShortId,
    string Image,
    bool IsAgentRuntime,
    string State,
    string? Health,
    double? CpuPercent,
    long? MemoryBytes,
    DateTimeOffset? UnusedSince,
    ProtectionReason? Protection,
    bool RetentionEligible);

/// <param name="SizeBytes">image 大小（含共用 layer）；不是實際可釋放的空間。</param>
/// <param name="HasSharedLayers">與同一 store 的其他 image 共用 layer：刪除後實際釋放的空間會比 SizeBytes 小。</param>
/// <param name="UnusedSince">持續沒有任何 container 引用的起算時間（首次觀測）；有引用時為 null。</param>
public sealed record ImageResource(
    string Store,
    string ShortId,
    IReadOnlyList<string> Tags,
    long SizeBytes,
    bool HasSharedLayers,
    int ContainerReferences,
    DateTimeOffset Created,
    DateTimeOffset? UnusedSince,
    ProtectionReason? Protection,
    bool RetentionEligible);

/// <summary>門檻目前的狀態（ADR-0025 §3）。</summary>
/// <param name="Key">cpu / memory / disk:{檔案系統代號}。</param>
/// <param name="Below">目前低於門檻。</param>
/// <param name="Triggered">已持續不足、觸發緊急回收（直到回到回復門檻）。</param>
public sealed record PressureStatus(string Key, bool Below, DateTimeOffset? BelowSince, bool Triggered);

public sealed record ResourceSnapshot(
    DateTimeOffset GeneratedAt,
    HostResources Host,
    IReadOnlyList<FilesystemResources> Filesystems,
    IReadOnlyList<StoreStatus> Stores,
    IReadOnlyList<ContainerResource> Containers,
    IReadOnlyList<ImageResource> Images,
    IReadOnlyList<PressureStatus> Pressure,
    ReclaimState ReclaimState,
    MaintenanceTiming Timing);

/// <summary>由部署設定決定、管理介面只顯示的時間參數（ADR-0025 §3）。</summary>
public sealed record MaintenanceTiming(
    int SampleIntervalSeconds,
    int SustainMinutes,
    int CooldownMinutes,
    double RecoveryMarginPercent,
    int MaxDeletionsPerRound,
    int KeepRecentVersions,
    int RetentionSweepMinutes);

/// <summary>
/// 管理員可調整的回收政策（ADR-0025 §3）。0 天代表不依期限回收（不是立即刪除）；門檻 0 代表停用該門檻。
/// </summary>
public sealed record ReclamationPolicy(
    bool Enabled,
    int ContainerRetentionDays,
    int ImageRetentionDays,
    double CpuMinFreePercent,
    double MemoryMinAvailableGb,
    double MemoryMinAvailablePercent,
    double DiskMinAvailableGb,
    double DiskMinAvailablePercent)
{
    public const int MaxRetentionDays = 3650;

    public static ReclamationPolicy Default { get; } = new(false, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>回傳錯誤訊息（中文，給管理員看）；通過時為 null。</summary>
    public string? Validate()
    {
        if (ContainerRetentionDays is < 0 or > MaxRetentionDays || ImageRetentionDays is < 0 or > MaxRetentionDays)
        {
            return $"保留天數必須在 0～{MaxRetentionDays} 之間（0 代表不依期限回收）。";
        }

        if (!IsPercent(CpuMinFreePercent) || !IsPercent(MemoryMinAvailablePercent) || !IsPercent(DiskMinAvailablePercent))
        {
            return "比例門檻必須在 0～99 之間（0 代表停用）。";
        }

        if (!IsGb(MemoryMinAvailableGb) || !IsGb(DiskMinAvailableGb))
        {
            return "容量門檻必須在 0～100000 GB 之間（0 代表停用）。";
        }

        return null;
    }

    private static bool IsPercent(double value) => double.IsFinite(value) && value is >= 0 and <= 99;

    private static bool IsGb(double value) => double.IsFinite(value) && value is >= 0 and <= 100_000;
}

public enum ReclaimTrigger
{
    Retention,
    Cpu,
    Memory,
    Disk,
}

public enum ReclaimItemResult
{
    Deleted,
    Failed,
    Skipped,
}

/// <summary>一次回收中處理的一個項目（摘要，不含 CLI 輸出）。</summary>
public sealed record ReclaimItem(string Store, string Kind, string Name, ReclaimItemResult Result, string? Reason);

public sealed record FilesystemDelta(string Id, long AvailableBeforeBytes, long AvailableAfterBytes);

/// <summary>
/// 每輪回收的持久紀錄（ADR-0025 §4）：觸發原因、候選 / 保護 / 刪除 / 失敗數、實際磁碟前後可用量與摘要；沒有候選也留摘要。
/// 不以 image 尺寸聲稱實際回收量，只記錄檔案系統實測的前後差。
/// </summary>
public sealed record ReclaimRun(
    long Id,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    ReclaimTrigger Trigger,
    int Candidates,
    int Protected,
    int Deleted,
    int Failed,
    IReadOnlyList<FilesystemDelta> Filesystems,
    IReadOnlyList<ReclaimItem> Items,
    string Summary);

/// <summary>Dry run：目前一般回收會刪除的項目，與緊急回收可考慮的安全候選（不實際刪除）。</summary>
public sealed record DryRunResult(
    DateTimeOffset GeneratedAt,
    ReclaimState ReclaimState,
    IReadOnlyList<ReclaimItem> RetentionCandidates,
    IReadOnlyList<ReclaimItem> EmergencyCandidates,
    IReadOnlyList<string> Notes);
