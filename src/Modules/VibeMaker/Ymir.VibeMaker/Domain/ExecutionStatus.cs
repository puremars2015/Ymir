namespace Ymir.VibeMaker.Domain;

/// <summary>AGENT_EXECUTION.status（SA §8）。</summary>
public enum ExecutionStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,
}

public static class ExecutionStatusExtensions
{
    public static bool IsTerminal(this ExecutionStatus status) =>
        status is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled;
}
