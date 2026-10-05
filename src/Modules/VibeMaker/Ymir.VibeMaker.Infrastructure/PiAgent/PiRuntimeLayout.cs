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

    public const string ApiKeyEnvironmentVariable = "LITELLM_API_KEY";
}
