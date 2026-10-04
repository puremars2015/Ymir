using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Infrastructure.Runtime;

namespace Ymir.VibeMaker.Infrastructure.Containers;

/// <summary>
/// 產生 container CLI 參數（Podman 與 Docker 共用，差異只在使用者對應）。集中在這裡以便單元測試驗證安全規則（SA §7、§12）：
/// drop 所有 capabilities、no-new-privileges、唯讀 root filesystem、資源限制、
/// 只掛載該 workspace 自己的目錄，永不掛載 container runtime socket 或 host 敏感路徑。
/// </summary>
internal static class ContainerCommandBuilder
{
    /// <summary>Containerfile 內 agent 使用者的 uid/gid。</summary>
    public const int AgentUid = 1000;

    public static string ContainerName(Guid workspaceId) => $"ymir-ws-{workspaceId:N}";

    public static IReadOnlyList<string> BuildRunArguments(
        RuntimeOptions options,
        Guid workspaceId,
        Guid runtimeId,
        WorkspaceDirectories directories)
    {
        ArgumentNullException.ThrowIfNull(options);
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
            "--name", ContainerName(workspaceId),
            "--hostname", "ymir-runtime",
            // 主程序 sleep 不處理 SIGTERM；--init 讓 init 程序當 PID 1，stop 才能立即結束。
            "--init",
            "--label", $"ymir.workspace-id={workspaceId:D}",
            "--label", $"ymir.runtime-id={runtimeId:D}",
            .. userMapping,
            "--cap-drop", "ALL",
            "--security-opt", "no-new-privileges",
            "--read-only",
            "--tmpfs", "/tmp:rw,size=512m",
            "--tmpfs", "/home/agent:rw,size=256m",
            "--pids-limit", options.PidsLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--memory", options.MemoryLimit,
            "--cpus", options.CpuLimit,
            "--network", options.ResolvedNetwork,
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
    public static IReadOnlyList<string> BuildDockerPrepareMountsArguments(RuntimeOptions options, WorkspaceDirectories directories)
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
    public static IReadOnlyList<string> BuildExecArguments(Guid workspaceId, RuntimeProcessSpec spec)
    {
        var arguments = new List<string> { "exec", "--interactive", "--workdir", RuntimePaths.Workspace };
        foreach (var name in spec.Environment?.Keys ?? Enumerable.Empty<string>())
        {
            arguments.Add("--env");
            arguments.Add(name);
        }

        arguments.Add(ContainerName(workspaceId));
        arguments.Add(spec.Executable);
        arguments.AddRange(spec.Arguments);
        return arguments;
    }

    public static IReadOnlyList<string> BuildInspectStatusArguments(Guid workspaceId) =>
        ["container", "inspect", "--format", "{{.State.Status}}", ContainerName(workspaceId)];

    public static IReadOnlyList<string> BuildStartArguments(Guid workspaceId) => ["start", ContainerName(workspaceId)];

    public static IReadOnlyList<string> BuildStopArguments(Guid workspaceId) => ["stop", "-t", "10", ContainerName(workspaceId)];

    public static IReadOnlyList<string> BuildRemoveArguments(Guid workspaceId) => ["rm", "--force", ContainerName(workspaceId)];
}
