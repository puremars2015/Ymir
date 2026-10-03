namespace Ymir.VibeMaker.Infrastructure.Runtime;

/// <summary>由 workspace id 推導 host 目錄。路徑只由 id 組成，避免任何外部輸入影響 host path。</summary>
internal sealed record WorkspaceDirectories(string Workspace, string AgentState)
{
    public static WorkspaceDirectories For(string workspaceRoot, Guid workspaceId)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var baseDirectory = Path.Combine(root, workspaceId.ToString("N"));
        return new WorkspaceDirectories(Path.Combine(baseDirectory, "workspace"), Path.Combine(baseDirectory, "agent-state"));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Workspace);
        Directory.CreateDirectory(AgentState);
    }
}
