using Ymir.VibeMaker.Application.Runtime;

namespace Ymir.VibeMaker.Infrastructure.PiAgent;

/// <summary>Pi 在 runtime 內的檔案配置（皆位於持久化的 <c>/agent-state</c>，container 重建後 Agent 不失憶）。</summary>
internal static class PiRuntimeLayout
{
    /// <summary><c>PI_CODING_AGENT_DIR</c>：models.json、auth.json 等，Pi 需要寫入權限。</summary>
    public const string AgentDirectory = RuntimePaths.AgentState + "/pi-agent";

    public const string SessionDirectory = RuntimePaths.AgentState + "/sessions";

    /// <summary>每次執行附加的 system prompt 檔案；執行結束後刪除。</summary>
    public const string PromptDirectory = RuntimePaths.AgentState + "/prompts";

    /// <summary>
    /// 使用者自建 MCP 設定（ADR-0012 B.3）：Agent 寫這個檔，Ymir 每次執行前依政策合併成 Pi 讀取的 <c>mcp.json</c>。
    /// </summary>
    public const string UserMcpFileName = "mcp.user.json";

    /// <summary>Ymir 每次執行前重寫的檔案（ADR-0012 A.3、Spike 結果 3）：Agent 改寫也只到下一次執行前有效。</summary>
    public const string McpConfigPath = AgentDirectory + "/mcp.json";

    public const string SettingsPath = AgentDirectory + "/settings.json";

    public const string TrustPath = AgentDirectory + "/trust.json";

    /// <summary>平台 skill（<c>ymir-extension-builder</c>）；不在 Pi 的 skill 掃描路徑內，只以 <c>--skill</c> 載入，每次執行前重寫。</summary>
    public const string ExtensionBuilderSkillDirectory = RuntimePaths.AgentState + "/ymir/skills/ymir-extension-builder";

    public const string ApiKeyEnvironmentVariable = "LITELLM_API_KEY";
}
