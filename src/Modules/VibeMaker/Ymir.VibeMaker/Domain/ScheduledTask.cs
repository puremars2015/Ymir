namespace Ymir.VibeMaker.Domain;

public enum ScheduleKind { Reminder, AgentRun }
public enum ScheduleStatus { PendingConfirmation, Active, Paused, Ended }

/// <summary>ADR-0032 N2：副作用完成才推進時間；租約逾期以同一冪等鍵恢復。</summary>
public sealed class ScheduledTask
{
    private ScheduledTask() { }
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid ConversationId { get; private set; }
    public ScheduleKind Kind { get; private set; }
    public string Prompt { get; private set; } = string.Empty;
    public string? ModelId { get; private set; }
    public string? ThinkingLevel { get; private set; }
    public string? CronExpression { get; private set; }
    public string TimeZone { get; private set; } = "UTC";
    public ScheduleStatus Status { get; private set; }
    public string CreatedBy { get; private set; } = "user";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? NextRunAt { get; private set; }
    public Guid? LastExecutionId { get; private set; }
    public void TrackExecution(Guid executionId) => LastExecutionId = executionId;
    public void RecordOutcome(ExecutionStatus status)
    {
        LastExecutionId = null;
        LastResult = status == ExecutionStatus.Completed ? "Agent 工作已完成" : status == ExecutionStatus.Cancelled ? "Agent 工作已取消" : "Agent 工作失敗";
        ConsecutiveFailures = status == ExecutionStatus.Failed ? ConsecutiveFailures + 1 : 0;
        if (ConsecutiveFailures >= 3 && Status == ScheduleStatus.Active) Status = ScheduleStatus.Paused;
    }
    public DateTimeOffset? LastRunAt { get; private set; }
    public string? LastResult { get; private set; }
    public int ConsecutiveFailures { get; private set; }
    public DateTimeOffset? LeaseUntil { get; private set; }
    public byte[] Version { get; private set; } = [];
    public static ScheduledTask Create(Guid userId, Guid conversationId, ScheduleKind kind, string prompt,
        string? modelId, string? thinkingLevel, string? cronExpression, string timeZone, DateTimeOffset next, DateTimeOffset now) => new()
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            ConversationId = conversationId,
            Kind = kind,
            Prompt = DomainGuard.RequiredText(prompt, Message.ContentMaxLength, nameof(prompt)),
            ModelId = modelId,
            ThinkingLevel = thinkingLevel,
            CronExpression = cronExpression,
            TimeZone = timeZone,
            NextRunAt = next,
            CreatedAt = now,
            Status = ScheduleStatus.Active,
        };
    public void Replace(ScheduledTask draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        Kind = draft.Kind; Prompt = draft.Prompt; ModelId = draft.ModelId; ThinkingLevel = draft.ThinkingLevel;
        CronExpression = draft.CronExpression; TimeZone = draft.TimeZone; NextRunAt = draft.NextRunAt;
        ConsecutiveFailures = 0;
    }
    public void Pause() => Status = ScheduleStatus.Paused;
    public void Resume(DateTimeOffset next) { Status = ScheduleStatus.Active; NextRunAt = next; ConsecutiveFailures = 0; }
    public void End() { Status = ScheduleStatus.Ended; NextRunAt = null; LeaseUntil = null; }
    public void ReleaseLease() => LeaseUntil = null;
    public void Claim(DateTimeOffset now) => LeaseUntil = now.AddMinutes(5);
    public void Finish(DateTimeOffset now, DateTimeOffset? next, string result, bool failed, bool resetFailures = true)
    {
        LastRunAt = now; LastResult = result; LeaseUntil = null; NextRunAt = next;
        ConsecutiveFailures = failed ? ConsecutiveFailures + 1 : resetFailures ? 0 : ConsecutiveFailures;
        if (next is null) Status = ScheduleStatus.Ended;
        else if (ConsecutiveFailures >= 3) Status = ScheduleStatus.Paused;
    }
}
