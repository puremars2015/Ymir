namespace Ymir.VibeMaker.Infrastructure.Runtime;

/// <summary>設定區段 <c>VibeMaker:Runtime</c>（對應 SA §17 的 Runtime:* / Workspace:*）。</summary>
public sealed class RuntimeOptions
{
    public const string SectionName = "VibeMaker:Runtime";

    /// <summary><c>Podman</c>（正式）或 <c>Local</c>（僅開發用，無隔離）。</summary>
    public RuntimeProvider Provider { get; set; } = RuntimeProvider.Podman;

    /// <summary>
    /// Host 上所有 workspace 的根目錄；每個 workspace 位於 <c>{WorkspaceRoot}/{workspaceId}</c>。
    /// 只有 Runtime Manager 會解析此路徑，API 不接受任何外部傳入的 host path（SA §12）。
    /// </summary>
    public string WorkspaceRoot { get; set; } = Path.Combine(Path.GetTempPath(), "ymir-workspaces");

    public string Image { get; set; } = "localhost/ymir/agent-runtime:dev";

    public string MemoryLimit { get; set; } = "2g";

    public string CpuLimit { get; set; } = "1.0";

    public int PidsLimit { get; set; } = 512;

    /// <summary>Podman network；需能連到 LiteLLM（SA §7）。</summary>
    public string Network { get; set; } = "slirp4netns";

    /// <summary>SELinux 主機需要以 <c>:Z</c> 重新標記 volume。</summary>
    public bool SelinuxRelabel { get; set; }

    public int IdleTimeoutMinutes { get; set; } = 30;

    /// <summary>單次 Agent execution 逾時（分鐘，可為小數）。</summary>
    public double ExecutionTimeoutMinutes { get; set; } = 30;

    public string PodmanExecutable { get; set; } = "podman";
}

public enum RuntimeProvider
{
    Podman = 0,

    /// <summary>直接在 host 執行，沒有任何隔離。只允許在 Development 環境使用。</summary>
    Local = 1,
}
