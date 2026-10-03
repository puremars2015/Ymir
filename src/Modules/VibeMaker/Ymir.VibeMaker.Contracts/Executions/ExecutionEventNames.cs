namespace Ymir.VibeMaker.Contracts.Executions;

/// <summary>SSE <c>event:</c> 名稱，對應 SA §10 SSE Event Contract。</summary>
public static class ExecutionEventNames
{
    public const string ExecutionStarted = "execution.started";
    public const string AssistantDelta = "assistant.delta";
    public const string ToolStarted = "tool.started";
    public const string ToolCompleted = "tool.completed";
    public const string Status = "status";
    public const string ExecutionCompleted = "execution.completed";
    public const string ExecutionFailed = "execution.failed";
    public const string ExecutionCancelled = "execution.cancelled";
}
