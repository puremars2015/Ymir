namespace Ymir.VibeMaker.Contracts.Executions;

/// <summary>錯誤碼，對應 SA §13。</summary>
public static class ExecutionErrorCodes
{
    public const string AuthRequired = "AUTH_REQUIRED";
    public const string Forbidden = "FORBIDDEN";
    public const string ConversationNotFound = "CONVERSATION_NOT_FOUND";
    public const string ExecutionConflict = "EXECUTION_CONFLICT";
    public const string RuntimeStartFailed = "RUNTIME_START_FAILED";
    public const string AgentTimeout = "AGENT_TIMEOUT";
    public const string AgentRuntimeError = "AGENT_RUNTIME_ERROR";
    public const string ModelProviderError = "MODEL_PROVIDER_ERROR";
    public const string ModelNotAvailable = "MODEL_NOT_AVAILABLE";

    /// <summary><c>/make</c> 的主題不存在或已停用。</summary>
    public const string MakeTopicNotAvailable = "MAKE_TOPIC_NOT_AVAILABLE";

    /// <summary>只送了 <c>/make</c>：前端應顯示主題按鈕，不會送出。</summary>
    public const string MakeDescriptionRequired = "MAKE_DESCRIPTION_REQUIRED";
    public const string ExecutionCancelled = "EXECUTION_CANCELLED";
}
