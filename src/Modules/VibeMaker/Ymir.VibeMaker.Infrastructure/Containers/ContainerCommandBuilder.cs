using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Infrastructure.Runtime;

namespace Ymir.VibeMaker.Infrastructure.Containers;

/// <summary>
/// 產生 container CLI 參數（Podman 與 Docker 共用，差異只在使用者對應）。集中在這裡以便單元測試驗證安全規則（SA §7、§12）：
/// drop 所有 capabilities、no-new-privileges、唯讀 root filesystem、資源限制、
/// 只掛載該使用者自己的目錄（一個使用者一個 container，ADR-0007），永不掛載 container runtime socket 或 host 敏感路徑。
/// </summary>
internal static class ContainerCommandBuilder
{
    /// <summary>Containerfile 內 agent 使用者的 uid/gid。</summary>
    public const int AgentUid = 1000;

    public static string ContainerName(Guid userId) => $"ymir-user-{userId:N}";

    /// <summary>記錄 container 建立時的對外連線模式；EnsureRuntime 據此判斷是否要依新的政策重建（ADR-0012 A.8）。</summary>
    public const string NetworkLabel = "ymir.network";

    public static string NetworkLabelValue(RuntimeNetworkAccess network) => network == RuntimeNetworkAccess.Restricted ? "restricted" : "internet";

    /// <summary>沒有 label 的既有 container（A1b 之前建立）視為可以對外連線。</summary>
    public static RuntimeNetworkAccess NetworkOfLabel(string? value) =>
        value == "restricted" ? RuntimeNetworkAccess.Restricted : RuntimeNetworkAccess.Internet;

    public static IReadOnlyList<string> BuildRunArguments(
        RuntimeOptions options,
        Guid userId,
        Guid runtimeId,
        UserDirectories directories,
        RuntimeNetworkAccess network = RuntimeNetworkAccess.Internet)
    {
        ArgumentNullException.ThrowIfNull(options);
        // 受限網路的名稱只來自部署設定；沒有設定時不退回成可以對外連線（ADR-0012 A.8）。
        var networkName = network == RuntimeNetworkAccess.Restricted
            ? options.RestrictedNetwork is { } restricted && RuntimeOptions.IsValidRestrictedNetworkName(restricted)
                ? restricted
                : throw new RuntimeNetworkUnavailableException("Restricted network is not configured (VibeMaker:Runtime:RestrictedNetwork).")
            : options.ResolvedNetwork;
        var isDocker = options.Provider == RuntimeProvider.Docker;
        // --mount 而非 --volume：Windows 路徑（C:\...）的冒號不會和 --volume 的分隔符號混淆。
        var relabel = options.SelinuxRelabel && !isDocker ? ",relabel=private" : string.Empty;

        // Rootless Podman：keep-id 讓 host 使用者對應到 container 的 agent（uid 1000），掛載目錄權限才正確。
        // Docker 沒有 keep-id，直接以 agent 使用者執行（ADR-0005）。
        string[] userMapping = isDocker
            ? ["--user", $"{AgentUid}:{AgentUid}"]
            : ["--userns", $"keep-id:uid={AgentUid},gid={AgentUid}"];

        return
        [
            "run",
            "--detach",
            "--name", ContainerName(userId),
            "--hostname", "ymir-runtime",
            // 主程序 sleep 不處理 SIGTERM；--init 讓 init 程序當 PID 1，stop 才能立即結束。
            "--init",
            "--label", $"ymir.user-id={userId:D}",
            "--label", $"ymir.runtime-id={runtimeId:D}",
            "--label", $"{NetworkLabel}={NetworkLabelValue(network)}",
            .. userMapping,
            "--cap-drop", "ALL",
            "--security-opt", "no-new-privileges",
            "--read-only",
            "--tmpfs", "/tmp:rw,size=512m",
            "--tmpfs", "/home/agent:rw,size=256m",
            "--pids-limit", options.PidsLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--memory", options.MemoryLimit,
            "--cpus", options.CpuLimit,
            "--network", networkName,
            "--mount", $"type=bind,source={directories.Workspace},target={RuntimePaths.Workspace}{relabel}",
            "--mount", $"type=bind,source={directories.AgentState},target={RuntimePaths.AgentState}{relabel}",
            options.Image,
            "sleep", "infinity",
        ];
    }

    /// <summary>
    /// 只用於 Docker：Docker 沒有 Podman 的 <c>keep-id</c>，host 上新建的目錄擁有者不一定是 uid 1000。
    /// 建立 runtime 前以一次性的 container（不連網路、只掛這兩個目錄）把擁有者改成 agent 使用者（ADR-0005）。
    /// </summary>
    public static IReadOnlyList<string> BuildDockerPrepareMountsArguments(RuntimeOptions options, UserDirectories directories)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(directories);
        return
        [
            "run",
            "--rm",
            "--user", "0:0",
            "--network", "none",
            "--mount", $"type=bind,source={directories.Workspace},target={RuntimePaths.Workspace}",
            "--mount", $"type=bind,source={directories.AgentState},target={RuntimePaths.AgentState}",
            options.Image,
            "chown", $"{AgentUid}:{AgentUid}", RuntimePaths.Workspace, RuntimePaths.AgentState,
        ];
    }

    /// <summary>
    /// <c>podman / docker exec -i</c>。環境變數只傳名稱（<c>-e NAME</c>），值由 podman 程序自己的環境繼承，
    /// 避免 secret 出現在 host 的程序參數列表（<c>ps</c>）中。
    /// </summary>
    public static IReadOnlyList<string> BuildExecArguments(Guid userId, RuntimeProcessSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        UserDirectories.EnsureAllowed(spec.WorkingDirectory);
        var arguments = new List<string> { "exec", "--interactive", "--workdir", spec.WorkingDirectory };
        foreach (var name in spec.Environment?.Keys ?? Enumerable.Empty<string>())
        {
            arguments.Add("--env");
            arguments.Add(name);
        }

        arguments.Add(ContainerName(userId));
        arguments.Add(spec.Executable);
        arguments.AddRange(spec.Arguments);
        return arguments;
    }

    /// <summary>
    /// 在 container 內以 agent 使用者建立工作目錄（專案 / 對話目錄，ADR-0007）。由 container 內建立，
    /// Docker 與 Podman 的擁有者都正確，不需要再以 root chown。路徑只接受由 Guid 產生的允許路徑。
    /// </summary>
    public static IReadOnlyList<string> BuildEnsureDirectoryArguments(Guid userId, string runtimeDirectory)
    {
        UserDirectories.EnsureAllowed(runtimeDirectory);
        return ["exec", ContainerName(userId), "mkdir", "-p", runtimeDirectory];
    }

    /// <summary>輸出 <c>狀態|network label</c>；Podman 與 Docker 的 Go template 對不存在的 label 都輸出空字串。</summary>
    public static IReadOnlyList<string> BuildInspectStatusArguments(Guid userId) =>
        ["container", "inspect", "--format", $"{{{{.State.Status}}}}|{{{{index .Config.Labels \"{NetworkLabel}\"}}}}", ContainerName(userId)];

    public static IReadOnlyList<string> BuildStartArguments(Guid userId) => ["start", ContainerName(userId)];

    public static IReadOnlyList<string> BuildStopArguments(Guid userId) => ["stop", "-t", "10", ContainerName(userId)];

    public static IReadOnlyList<string> BuildRemoveArguments(Guid userId) => ["rm", "--force", ContainerName(userId)];
}
