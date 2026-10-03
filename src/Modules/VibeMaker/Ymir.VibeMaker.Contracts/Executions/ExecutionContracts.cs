namespace Ymir.VibeMaker.Contracts.Executions;

/// <param name="Status">取消請求處理後的狀態（RUNNING 表示已通知 Agent 中止，稍後會收到 execution.cancelled）。</param>
public sealed record CancelExecutionResponse(Guid ExecutionId, string Status);
