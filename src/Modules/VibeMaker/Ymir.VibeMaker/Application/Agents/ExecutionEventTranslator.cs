using Ymir.VibeMaker.Contracts.Executions;

namespace Ymir.VibeMaker.Application.Agents;

/// <summary>把 harness 事件轉成對外的 SSE 事件契約（SA §10）。</summary>
public static class ExecutionEventTranslator
{
    /// <param name="assistantMessageId">已保存的 ASSISTANT MESSAGE id，只用於 completed 事件。</param>
    public static ExecutionEvent ToExecutionEvent(this AgentEvent agentEvent, Guid executionId, Guid? assistantMessageId = null) =>
        agentEvent switch
        {
            AgentStarted => new ExecutionStartedEvent(executionId),
            AgentTextDelta e => new AssistantDeltaEvent(e.Text),
            AgentToolStarted e => new ToolStartedEvent(e.Tool, e.CallId, e.Summary),
            AgentToolCompleted e => new ToolCompletedEvent(e.CallId, e.Success),
            AgentStatus e => new StatusEvent(e.Text),
            AgentCompleted => new ExecutionCompletedEvent(executionId, assistantMessageId),
            AgentFailed e => new ExecutionFailedEvent(e.Code, e.Message),
            AgentCancelled => new ExecutionCancelledEvent(executionId),
            _ => throw new ArgumentOutOfRangeException(nameof(agentEvent), agentEvent.GetType().Name, "Unknown agent event"),
        };
}
