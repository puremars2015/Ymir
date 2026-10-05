using System.Text.RegularExpressions;

namespace Ymir.VibeMaker.Application.Runtime;

/// <summary>Runtime 內部的固定路徑（container 內），所有 provider 必須提供相同的檔案配置。</summary>
public static partial class RuntimePaths
{
    /// <summary>使用者的工作目錄根（持久化），一個使用者一份（ADR-0007）。</summary>
    public const string Workspace = "/workspace";

    /// <summary>Agent 狀態（Pi session、Pi agent 設定），持久化且與 workspace 分開。</summary>
    public const string AgentState = "/agent-state";

    /// <summary>專案的檔案群組：同一專案的對話共用（ADR-0007）。</summary>
    public static string ProjectDirectory(Guid projectId) => $"{Workspace}/projects/{projectId:N}";

    /// <summary>未分組對話自己的目錄：聊天之間不共用檔案（ADR-0007）。</summary>
    public static string ConversationDirectory(Guid conversationId) => $"{Workspace}/chats/{conversationId:N}";

    /// <summary>對話的 Agent 工作目錄：有專案用專案目錄，否則用對話自己的目錄。</summary>
    public static string WorkingDirectoryFor(Guid conversationId, Guid? projectId) =>
        projectId is { } id ? ProjectDirectory(id) : ConversationDirectory(conversationId);

    /// <summary>
    /// 工作目錄只能是 <c>/workspace</c> 或上面兩種由 Guid 產生的子目錄；runtime manager 以此拒絕任何其他路徑，
    /// 避免外部輸入影響 host 路徑（SA §12）。
    /// </summary>
    public static bool IsAllowedWorkingDirectory(string path) =>
        path == Workspace || AllowedSubdirectory().IsMatch(path);

    [GeneratedRegex("^/workspace/(projects|chats)/[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedSubdirectory();
}
