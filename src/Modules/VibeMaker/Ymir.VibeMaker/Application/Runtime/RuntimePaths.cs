namespace Ymir.VibeMaker.Application.Runtime;

/// <summary>Runtime 內部的固定路徑（container 內），所有 provider 必須提供相同的檔案配置。</summary>
public static class RuntimePaths
{
    /// <summary>使用者的工作目錄（持久化）。</summary>
    public const string Workspace = "/workspace";

    /// <summary>Agent 狀態（Pi session、Pi agent 設定），持久化且與 workspace 分開。</summary>
    public const string AgentState = "/agent-state";
}
