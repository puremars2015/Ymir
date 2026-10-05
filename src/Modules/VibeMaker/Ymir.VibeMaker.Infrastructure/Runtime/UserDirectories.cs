using Ymir.VibeMaker.Application.Runtime;

namespace Ymir.VibeMaker.Infrastructure.Runtime;

/// <summary>
/// 由 user id 推導 host 目錄（一個使用者一個 runtime，ADR-0007）：<c>{WorkspaceRoot}/users/{userId}/workspace</c> 與 <c>agent-state</c>。
/// 路徑只由 id 組成，避免任何外部輸入影響 host path（SA §12）。
/// </summary>
internal sealed record UserDirectories(string Workspace, string AgentState)
{
    public static UserDirectories For(string workspaceRoot, Guid userId)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var baseDirectory = Path.Combine(root, "users", userId.ToString("N"));
        return new UserDirectories(Path.Combine(baseDirectory, "workspace"), Path.Combine(baseDirectory, "agent-state"));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Workspace);
        Directory.CreateDirectory(AgentState);
    }

    /// <summary>把允許的 runtime 工作目錄（<see cref="RuntimePaths.IsAllowedWorkingDirectory"/>）換成 host 路徑。</summary>
    public string HostPathOf(string runtimeWorkingDirectory)
    {
        EnsureAllowed(runtimeWorkingDirectory);
        var relative = runtimeWorkingDirectory[RuntimePaths.Workspace.Length..].TrimStart('/');
        return relative.Length == 0 ? Workspace : Path.Combine(Workspace, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    public static void EnsureAllowed(string runtimeWorkingDirectory)
    {
        if (!RuntimePaths.IsAllowedWorkingDirectory(runtimeWorkingDirectory))
        {
            throw new ArgumentException("Working directory is not an allowed runtime path.", nameof(runtimeWorkingDirectory));
        }
    }
}
