using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Infrastructure.Runtime;

namespace Ymir.VibeMaker.Infrastructure.Podman;

/// <summary>
/// 產生 podman CLI 參數。集中在這裡以便單元測試驗證安全規則（SA §7、§12）：
/// rootless、drop 所有 capabilities、no-new-privileges、唯讀 root filesystem、資源限制、
/// 只掛載該 workspace 自己的目錄，永不掛載 podman socket 或 host 敏感路徑。
/// </summary>
internal static class PodmanCommandBuilder
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
        var volumeSuffix = options.SelinuxRelabel ? ",Z" : string.Empty;
        return
        [
            "run",
            "--detach",
            "--name", ContainerName(workspaceId),
            "--hostname", "ymir-runtime",
            // 主程序 sleep 不處理 SIGTERM；--init 讓 catatonit 當 PID 1，podman stop 才能立即結束。
            "--init",
            "--label", $"ymir.workspace-id={workspaceId:D}",
            "--label", $"ymir.runtime-id={runtimeId:D}",
            "--userns", $"keep-id:uid={AgentUid},gid={AgentUid}",
            "--cap-drop", "ALL",
            "--security-opt", "no-new-privileges",
            "--read-only",
            "--tmpfs", "/tmp:rw,size=512m",
            "--tmpfs", "/home/agent:rw,size=256m",
            "--pids-limit", options.PidsLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--memory", options.MemoryLimit,
            "--cpus", options.CpuLimit,
            "--network", options.Network,
            "--volume", $"{directories.Workspace}:{RuntimePaths.Workspace}:rw{volumeSuffix}",
            "--volume", $"{directories.AgentState}:{RuntimePaths.AgentState}:rw{volumeSuffix}",
            options.Image,
            "sleep", "infinity",
        ];
    }

    /// <summary>
    /// <c>podman exec -i</c>。環境變數只傳名稱（<c>-e NAME</c>），值由 podman 程序自己的環境繼承，
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

    public static IReadOnlyList<string> BuildStopArguments(Guid workspaceId) => ["stop", "--time", "10", ContainerName(workspaceId)];

    public static IReadOnlyList<string> BuildRemoveArguments(Guid workspaceId) => ["rm", "--force", ContainerName(workspaceId)];
}
