namespace Ymir.VibeMaker.Domain;

/// <summary>
/// 一次 Agent 執行（SA §8 AGENT_EXECUTION）。狀態只能依 QUEUED → RUNNING → (COMPLETED | FAILED | CANCELLED) 前進，
/// QUEUED 也可直接取消或失敗；非法轉移拋出 <see cref="InvalidOperationException"/>，避免 execution 卡在中間狀態（SA §14）。
/// </summary>
public sealed class AgentExecution
{
    private AgentExecution()
    {
    }

    public Guid Id { get; private set; }

    public Guid ConversationId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid UserMessageId { get; private set; }

    /// <summary>前端產生的冪等鍵；(user_id, client_request_id) 唯一（SA §9.1）。</summary>
    public Guid ClientRequestId { get; private set; }

    public Guid? AgentSessionId { get; private set; }

    public Guid? RuntimeId { get; private set; }

    public ExecutionStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    public string? ErrorCode { get; private set; }

    /// <summary>最後保存的 ASSISTANT 訊息。</summary>
    public Guid? AssistantMessageId { get; private set; }

    /// <summary>這次執行使用的模型（稽核與用量分析）；舊資料為 null，代表當時的預設模型。</summary>
    public string? ModelId { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public static AgentExecution Queue(Conversation conversation, Message userMessage, Guid clientRequestId, DateTimeOffset now, string? modelId = null)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(userMessage);
        var execution = new AgentExecution
        {
            Id = Guid.CreateVersion7(now),
            ConversationId = conversation.Id,
            UserId = conversation.UserId,
            UserMessageId = userMessage.Id,
            ClientRequestId = clientRequestId,
            Status = ExecutionStatus.Queued,
            CreatedAt = now,
            ModelId = modelId,
        };
        userMessage.AttachExecution(execution.Id);
        return execution;
    }

    public void Start(Guid agentSessionId, Guid runtimeId, DateTimeOffset now)
    {
        EnsureStatus(ExecutionStatus.Queued);
        Status = ExecutionStatus.Running;
        AgentSessionId = agentSessionId;
        RuntimeId = runtimeId;
        StartedAt = now;
    }

    public void Complete(Guid? assistantMessageId, DateTimeOffset now)
    {
        EnsureStatus(ExecutionStatus.Running);
        Status = ExecutionStatus.Completed;
        AssistantMessageId = assistantMessageId;
        EndedAt = now;
    }

    public void Fail(string errorCode, Guid? assistantMessageId, DateTimeOffset now)
    {
        EnsureStatus(ExecutionStatus.Queued, ExecutionStatus.Running);
        Status = ExecutionStatus.Failed;
        ErrorCode = errorCode;
        AssistantMessageId = assistantMessageId;
        EndedAt = now;
    }

    public void Cancel(Guid? assistantMessageId, DateTimeOffset now)
    {
        EnsureStatus(ExecutionStatus.Queued, ExecutionStatus.Running);
        Status = ExecutionStatus.Cancelled;
        AssistantMessageId = assistantMessageId;
        EndedAt = now;
    }

    private void EnsureStatus(params ExecutionStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new InvalidOperationException($"Execution {Id} cannot transition from {Status}.");
        }
    }
}
