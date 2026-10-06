namespace Ymir.VibeMaker.Infrastructure.Runtime;

/// <summary>設定區段 <c>VibeMaker:Runtime</c>（對應 SA §17 的 Runtime:* / Workspace:*）。</summary>
public sealed class RuntimeOptions
{
    public const string SectionName = "VibeMaker:Runtime";

    /// <summary>
    /// <c>Podman</c>（正式環境，rootless）、<c>Docker</c>（只用於開發 / 驗證，例如 Windows Docker Desktop，ADR-0005）
    /// 或 <c>Local</c>（僅開發用，無隔離）。
    /// </summary>
    public RuntimeProvider Provider { get; set; } = RuntimeProvider.Podman;

    /// <summary>
    /// Host 上所有使用者資料的根目錄；每個使用者位於 <c>{WorkspaceRoot}/users/{userId}</c>（一個使用者一個 runtime，ADR-0007）。
    /// 只有 Runtime Manager 會解析此路徑，API 不接受任何外部傳入的 host path（SA §12）。
    /// </summary>
    public string WorkspaceRoot { get; set; } = Path.Combine(Path.GetTempPath(), "ymir-workspaces");

    public string Image { get; set; } = "localhost/ymir/agent-runtime:dev";

    public string MemoryLimit { get; set; } = "2g";

    public string CpuLimit { get; set; } = "1.0";

    public int PidsLimit { get; set; } = 512;

    /// <summary>
    /// Container network；需能連到 LiteLLM（SA §7）。未設定時 Podman 用 <c>slirp4netns</c>、Docker 用 <c>bridge</c>。
    /// </summary>
    public string? Network { get; set; }

    /// <summary>SELinux 主機需要重新標記掛載目錄（<c>relabel=private</c>，等同 <c>:Z</c>；只適用 Podman）。</summary>
    public bool SelinuxRelabel { get; set; }

    /// <summary>
    /// 沒有 execution 多久後自動停止使用者的 runtime（分鐘，0 表示不停止；檔案保留，下一次送訊息時自動啟動）。
    /// 以下四項是部署預設值，管理介面儲存的執行政策優先（ADR-0011）。
    /// </summary>
    public double IdleTimeoutMinutes { get; set; } = 30;

    /// <summary>單次 Agent execution 逾時（分鐘，可為小數）。</summary>
    public double ExecutionTimeoutMinutes { get; set; } = 30;

    /// <summary>每位使用者同時排隊 + 執行中的 execution 上限。</summary>
    public int MaxPendingExecutionsPerUser { get; set; } = 5;

    /// <summary>每位使用者過去 24 小時可建立的 execution 數；0 表示不限制。</summary>
    public int DailyExecutionLimit { get; set; }

    /// <summary>閒置檢查間隔（秒）。</summary>
    public double IdleCheckIntervalSeconds { get; set; } = 60;

    /// <summary>Container CLI；未設定時依 <see cref="Provider"/> 使用 <c>podman</c> 或 <c>docker</c>。</summary>
    public string? ContainerExecutable { get; set; }

    /// <summary><see cref="RuntimeProvider.Remote"/> 時連線的 runtime host（ADR-0008）。</summary>
    public RemoteRuntimeOptions Remote { get; set; } = new();

    internal string ResolvedNetwork => Network ?? (Provider == RuntimeProvider.Docker ? "bridge" : "slirp4netns");

    internal string ResolvedExecutable => ContainerExecutable ?? (Provider == RuntimeProvider.Docker ? "docker" : "podman");
}

public enum RuntimeProvider
{
    Podman = 0,

    /// <summary>直接在 host 執行，沒有任何隔離。只允許在 Development 環境使用。</summary>
    Local = 1,

    /// <summary>Docker（非 rootless）。只用於沒有 Linux 主機時的開發與驗證，正式環境不支援（ADR-0005）。</summary>
    Docker = 2,

    /// <summary>
    /// 呼叫主機上的 runtime host 服務（<c>Ymir.RuntimeHost</c>，ADR-0008）。API 在容器內執行時使用：
    /// API 不需要 Podman，也不掛載任何 container runtime socket。
    /// </summary>
    Remote = 3,
}

/// <summary>Runtime host 的連線設定（ADR-0008）。</summary>
public sealed class RemoteRuntimeOptions
{
    /// <summary><c>unix:/run/ymir-runtime/runtime.sock</c>（建議）或 loopback 的 <c>http://127.0.0.1:5090</c>。</summary>
    public string? Endpoint { get; set; }

    /// <summary>與 runtime host 共用的 bearer token（至少 32 字元），只放在部署環境的 secret / .env。</summary>
    public string? Token { get; set; }
}
