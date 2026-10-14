using Ymir.Maintenance;

namespace Ymir.MaintenanceHost;

/// <summary>
/// 設定區段 <c>MaintenanceHost</c>（ADR-0025 §2）。可管理的 store、帳號、image repository、container 與正式設定檔位置
/// 全部只來自這裡；Token 只放在部署 secret（<c>/etc/ymir/maintenance-host.env</c>），不得進版控。
/// </summary>
public sealed class MaintenanceHostOptions
{
    public const string SectionName = "MaintenanceHost";

    /// <summary><c>unix:/run/ymir-maintenance/maintenance.sock</c> 或 loopback 的 <c>http://127.0.0.1:5091</c>。</summary>
    public string? Listen { get; set; }

    public string? Token { get; set; }

    /// <summary>Unix socket 的權限（八進位），預設 660；不允許 other 存取。</summary>
    public string? SocketMode { get; set; }

    /// <summary>觀測狀態、政策與回收紀錄的保存位置（root 600）。</summary>
    public string StateDirectory { get; set; } = "/var/lib/ymir-maintenance";

    /// <summary>
    /// 與 runtime host、部署腳本共用的生命週期鎖目錄（<see cref="LifecycleLock"/>）。沒有設定或不存在時只提供監控與 dry run。
    /// </summary>
    public string? LockDirectory { get; set; }

    /// <summary>container CLI 的執行檔（預設 podman）。</summary>
    public string ContainerExecutable { get; set; } = "podman";

    public int SampleIntervalSeconds { get; set; } = 30;

    public int SustainMinutes { get; set; } = 5;

    public int CooldownMinutes { get; set; } = 10;

    /// <summary>回復門檻：比例門檻加上這個百分點、容量門檻加上這個百分比後才算回復（遲滯）。</summary>
    public double RecoveryMarginPercent { get; set; } = 5;

    public int MaxDeletionsPerRound { get; set; } = 10;

    /// <summary>每個 Ymir repository 至少保留的最近版本數；小於 3 時以 3 計（ADR-0025 §4）。</summary>
    public int KeepRecentVersions { get; set; } = 3;

    /// <summary>依保留天數回收的檢查間隔。</summary>
    public int RetentionSweepMinutes { get; set; } = 60;

    /// <summary>保留的回收紀錄數。</summary>
    public int MaxRunRecords { get; set; } = 500;

    public List<MaintenanceStoreOptions> Stores { get; set; } = [];

    /// <summary>部署盤點登記的既有 Ymir image（沒有 <c>ymir.managed</c> label 的舊 image），以完整或 12 碼以上的 image ID 表示。</summary>
    public List<string> RegisteredImages { get; set; } = [];

    /// <summary>明確保護的回復版本（image ID）。</summary>
    public List<string> ProtectedImages { get; set; } = [];

    /// <summary>明確排除、永不回收的 container 名稱（例如 <c>ymir-sql-old</c>）。</summary>
    public List<string> ExcludedContainers { get; set; } = [];

    public MaintenanceTiming ToTiming() => new(
        SampleIntervalSeconds,
        SustainMinutes,
        CooldownMinutes,
        RecoveryMarginPercent,
        MaxDeletionsPerRound,
        EffectiveKeepRecentVersions,
        RetentionSweepMinutes);

    public int EffectiveKeepRecentVersions => Math.Max(3, KeepRecentVersions);

    /// <summary>啟動時檢查；錯誤的設定拒絕啟動，而不是以不安全的預設值運作。</summary>
    public void Validate()
    {
        MaintenanceProtocol.EnsureTokenIsStrong(Token, "MaintenanceHost:Token");
        if (!Path.IsPathFullyQualified(StateDirectory))
        {
            throw new InvalidOperationException("MaintenanceHost:StateDirectory must be an absolute path.");
        }

        if (LockDirectory is not null && !Path.IsPathFullyQualified(LockDirectory))
        {
            throw new InvalidOperationException("MaintenanceHost:LockDirectory must be an absolute path.");
        }

        if (SampleIntervalSeconds is < 5 or > 3600 || SustainMinutes is < 0 or > 1440 || CooldownMinutes is < 1 or > 1440
            || RecoveryMarginPercent is < 0 or > 50 || MaxDeletionsPerRound is < 1 or > 100 || RetentionSweepMinutes is < 5 or > 10080
            || MaxRunRecords is < 10 or > 10000)
        {
            throw new InvalidOperationException("MaintenanceHost timing settings are out of range.");
        }

        if (Stores.Count == 0)
        {
            throw new InvalidOperationException("MaintenanceHost:Stores must list at least one store.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var store in Stores)
        {
            store.Validate();
            if (!names.Add(store.Name!))
            {
                throw new InvalidOperationException($"MaintenanceHost store '{store.Name}' is listed twice.");
            }
        }
    }
}

/// <summary>一個 Podman store：root（rootful）或某個帳號的 rootless store。</summary>
public sealed class MaintenanceStoreOptions
{
    /// <summary>顯示名稱（例如 root、ymir、itadmin）。</summary>
    public string? Name { get; set; }

    /// <summary>rootless store 的帳號；<c>root</c> 或空白代表 rootful store。</summary>
    public string? Account { get; set; }

    /// <summary>屬於 Ymir 的 image repository（例如 <c>localhost/ymir/api</c>）；只有這些 repository 的 image 會被列出與考慮回收。</summary>
    public List<string> ImageRepositories { get; set; } = [];

    /// <summary>要顯示的 Ymir 服務 container 名稱（API、入口、Tunnel、資料庫等）；只監控，永不回收。</summary>
    public List<string> ServiceContainers { get; set; } = [];

    /// <summary>這個 store 是否有每位使用者的 Agent runtime container（<c>ymir-user-*</c>）。</summary>
    public bool AgentContainers { get; set; }

    /// <summary>Agent workspace 的根目錄（與 runtime host 的 <c>VibeMaker:Runtime:WorkspaceRoot</c> 相同）；用來確認 workspace 是外置掛載。</summary>
    public string? AgentWorkspaceRoot { get; set; }

    /// <summary>正式設定指向的 image：讀取這些檔案中 <c>Key=value</c> 的值（Quadlet 的 Image=、runtime host 的 VibeMaker__Runtime__Image）。</summary>
    public List<PinnedImageSource> PinnedImageSources { get; set; } = [];

    public bool IsRootful => string.IsNullOrWhiteSpace(Account) || Account == "root";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 32 || !Name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            throw new InvalidOperationException("MaintenanceHost store Name must be 1-32 letters, digits, '-' or '_'.");
        }

        if (!IsRootful && !Account!.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
        {
            throw new InvalidOperationException($"MaintenanceHost store '{Name}' has an invalid Account.");
        }

        if (ImageRepositories.Any(r => string.IsNullOrWhiteSpace(r) || r.Contains(':', StringComparison.Ordinal) || r.Contains('*', StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"MaintenanceHost store '{Name}' ImageRepositories must be exact repository names without tag or wildcard.");
        }

        if (AgentContainers && (AgentWorkspaceRoot is null || !Path.IsPathFullyQualified(AgentWorkspaceRoot)))
        {
            throw new InvalidOperationException($"MaintenanceHost store '{Name}' needs an absolute AgentWorkspaceRoot when AgentContainers is enabled.");
        }

        if (PinnedImageSources.Any(p => p.File is null || !Path.IsPathFullyQualified(p.File) || string.IsNullOrWhiteSpace(p.Key)))
        {
            throw new InvalidOperationException($"MaintenanceHost store '{Name}' PinnedImageSources need an absolute File and a Key.");
        }
    }
}

public sealed class PinnedImageSource
{
    public string? File { get; set; }

    public string? Key { get; set; }
}
