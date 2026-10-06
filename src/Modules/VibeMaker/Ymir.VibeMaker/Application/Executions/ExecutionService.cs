using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Auditing;
using Ymir.Platform.Identity;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Application.Make;
using Ymir.VibeMaker.Application.Models;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Contracts.Conversations;
using Ymir.VibeMaker.Contracts.Executions;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Executions;

/// <summary>送出訊息、取消、讀取事件等 execution 用例（SA §9、§11、§14）。只處理目前使用者自己的資料。</summary>
public sealed class ExecutionService(
    IVibeMakerDbContext db,
    ICurrentUser currentUser,
    IUserDirectory users,
    IExecutionDispatcher queue,
    IExecutionCancellationRegistry cancellations,
    ExecutionEventWriter eventWriter,
    IAuditLog auditLog,
    ModelCatalog models,
    MakeTopicService makeTopics,
    TimeProvider timeProvider)
{
    public static string EventStreamUrl(Guid executionId) => $"/api/executions/{executionId}/events";

    /// <summary>
    /// 保存 USER 訊息並建立 QUEUED execution（同一個 transaction），交給背景 worker 執行（SA §11）。
    /// 相同 clientRequestId 重送回傳原本的結果；同一對話已有執行中的 execution 時回傳 Conflict（由 filtered unique index 保證）。
    /// </summary>
    public async Task<SubmitMessageResult> SubmitAsync(Guid conversationId, SendMessageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ClientRequestId == Guid.Empty)
        {
            throw new DomainValidationException("clientRequestId is required.");
        }

        var userId = currentUser.UserId;
        var user = await users.FindAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null || user.Status == UserStatus.Disabled)
        {
            return SubmitMessageResult.Forbidden; // 停用的使用者不得建立 execution（SA §12）
        }

        if (await FindExistingAsync(userId, request.ClientRequestId, cancellationToken).ConfigureAwait(false) is { } existing)
        {
            return existing;
        }

        if (request.ModelId is not null && !models.IsAvailable(request.ModelId))
        {
            return SubmitMessageResult.ModelNotAvailable;
        }

        // /make 指令：由後端依主題組合送給 Agent 的完整指示；對話紀錄只保存使用者輸入的文字。
        string? agentPrompt = null;
        var makeDescription = MakePromptBuilder.ParseDescription(request.Content);
        if (request.MakeTopicId is { } topicId)
        {
            var topic = await makeTopics.FindEnabledPromptAsync(topicId, cancellationToken).ConfigureAwait(false);
            if (topic is null)
            {
                return SubmitMessageResult.MakeTopicNotAvailable;
            }

            var extra = string.Equals(makeDescription, topic.Name, StringComparison.Ordinal) ? null : makeDescription;
            agentPrompt = MakePromptBuilder.ForTopic(topic, extra);
        }
        else if (makeDescription is not null)
        {
            if (makeDescription.Length == 0)
            {
                return SubmitMessageResult.MakeDescriptionRequired;
            }

            agentPrompt = MakePromptBuilder.ForDescription(makeDescription, await makeTopics.ListEnabledPromptsAsync(cancellationToken).ConfigureAwait(false));
        }

        var conversation = await db.Conversations.SingleOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId && c.Status == ConversationStatus.Active, cancellationToken)
            .ConfigureAwait(false);
        if (conversation is null)
        {
            return SubmitMessageResult.NotFound;
        }

        var now = timeProvider.GetUtcNow();
        var nextSequence = await db.Messages.Where(m => m.ConversationId == conversationId)
            .MaxAsync(m => (long?)m.SequenceNo, cancellationToken).ConfigureAwait(false) ?? 0;
        var message = Message.CreateUser(conversationId, request.Content, nextSequence + 1, now);
        if (request.ModelId is not null)
        {
            conversation.SelectModel(request.ModelId);
        }

        // 執行時的模型：這次選的 → 對話上次選的 → 預設；已不在清單的模型退回預設。
        var execution = AgentExecution.Queue(conversation, message, request.ClientRequestId, now, models.Resolve(request.ModelId ?? conversation.ModelId), agentPrompt);
        conversation.Touch(now);
        db.Messages.Add(message);
        db.AgentExecutions.Add(execution);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // 由唯一索引擋下：可能是同一個 clientRequestId 同時送兩次，或同對話已有執行中的 execution。
            DetachAll();
            return await FindExistingAsync(userId, request.ClientRequestId, cancellationToken).ConfigureAwait(false)
                ?? SubmitMessageResult.Conflict;
        }

        await queue.EnqueueAsync(execution.Id, cancellationToken).ConfigureAwait(false);
        await auditLog.WriteAsync(new AuditEntry(currentUser.ActorName, "execution.create", "execution", execution.Id.ToString("D"), AuditResult.Success, now, null), cancellationToken)
            .ConfigureAwait(false);
        return SubmitMessageResult.Accept(new SendMessageResponse(message.Id, execution.Id, EventStreamUrl(execution.Id)));
    }

    /// <summary>
    /// 取消 execution（SA §5 Stop）：QUEUED 直接標成 CANCELLED；RUNNING 通知 worker 中止 Agent；已結束則不變（冪等）。
    /// </summary>
    public async Task<CancelExecutionResponse?> CancelAsync(Guid executionId, CancellationToken cancellationToken)
    {
        var execution = await FindOwnedAsync(executionId, tracking: true, cancellationToken).ConfigureAwait(false);
        if (execution is null)
        {
            return null;
        }

        await CancelCoreAsync(execution, currentUser.ActorName, cancellationToken).ConfigureAwait(false);
        return new CancelExecutionResponse(executionId, SaValues.Of(execution.Status));
    }

    /// <summary>
    /// 取消某位使用者所有尚未結束的 execution（Admin 停用帳號時，SA §12）。不檢查擁有者，只能由已授權的管理流程呼叫。
    /// </summary>
    public async Task<int> CancelAllForUserAsync(Guid userId, string actor, CancellationToken cancellationToken)
    {
        var active = await db.AgentExecutions
            .Where(e => e.UserId == userId && (e.Status == ExecutionStatus.Queued || e.Status == ExecutionStatus.Running))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var execution in active)
        {
            await CancelCoreAsync(execution, actor, cancellationToken).ConfigureAwait(false);
        }

        return active.Count;
    }

    private async Task CancelCoreAsync(AgentExecution execution, string actor, CancellationToken cancellationToken)
    {
        var executionId = execution.Id;
        var now = timeProvider.GetUtcNow();
        if (execution.Status == ExecutionStatus.Queued)
        {
            execution.Cancel(null, now);
            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                var sequence = await NextEventSequenceAsync(executionId, cancellationToken).ConfigureAwait(false);
                await eventWriter.AppendAsync(executionId, sequence, new ExecutionCancelledEvent(executionId), cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                // worker 剛好開始執行：改走執行中的取消流程。
                cancellations.TryCancel(executionId);
            }
        }
        else if (execution.Status == ExecutionStatus.Running)
        {
            cancellations.TryCancel(executionId);
        }

        await auditLog.WriteAsync(new AuditEntry(actor, "execution.cancel", "execution", executionId.ToString("D"), AuditResult.Success, now, null), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>目前使用者是否可以讀取此 execution 的事件。</summary>
    public async Task<bool> CanReadAsync(Guid executionId, CancellationToken cancellationToken) =>
        await FindOwnedAsync(executionId, tracking: false, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<IReadOnlyList<StoredExecutionEvent>> GetEventsAfterAsync(Guid executionId, long afterSequence, CancellationToken cancellationToken)
    {
        var records = await db.ExecutionEvents.AsNoTracking()
            .Where(e => e.ExecutionId == executionId && e.Sequence > afterSequence)
            .OrderBy(e => e.Sequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return records.Select(r => new StoredExecutionEvent(r.ExecutionId, r.Sequence, r.EventType, r.Data, ExecutionEventWriter.IsTerminal(r.EventType))).ToList();
    }

    private async Task<long> NextEventSequenceAsync(Guid executionId, CancellationToken cancellationToken) =>
        (await db.ExecutionEvents.Where(e => e.ExecutionId == executionId).MaxAsync(e => (long?)e.Sequence, cancellationToken).ConfigureAwait(false) ?? 0) + 1;

    private Task<AgentExecution?> FindOwnedAsync(Guid executionId, bool tracking, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var query = tracking ? db.AgentExecutions : db.AgentExecutions.AsNoTracking();
        return query.SingleOrDefaultAsync(e => e.Id == executionId && e.UserId == userId, cancellationToken);
    }

    private async Task<SubmitMessageResult?> FindExistingAsync(Guid userId, Guid clientRequestId, CancellationToken cancellationToken)
    {
        var existing = await db.AgentExecutions.AsNoTracking()
            .SingleOrDefaultAsync(e => e.UserId == userId && e.ClientRequestId == clientRequestId, cancellationToken)
            .ConfigureAwait(false);
        return existing is null
            ? null
            : SubmitMessageResult.Accept(new SendMessageResponse(existing.UserMessageId, existing.Id, EventStreamUrl(existing.Id)));
    }

    private void DetachAll()
    {
        if (db is DbContext context)
        {
            context.ChangeTracker.Clear();
        }
    }
}

public sealed record SubmitMessageResult(SubmitMessageOutcome Outcome, SendMessageResponse? Response)
{
    public static readonly SubmitMessageResult NotFound = new(SubmitMessageOutcome.NotFound, null);
    public static readonly SubmitMessageResult Conflict = new(SubmitMessageOutcome.Conflict, null);
    public static readonly SubmitMessageResult Forbidden = new(SubmitMessageOutcome.Forbidden, null);
    public static readonly SubmitMessageResult ModelNotAvailable = new(SubmitMessageOutcome.ModelNotAvailable, null);
    public static readonly SubmitMessageResult MakeTopicNotAvailable = new(SubmitMessageOutcome.MakeTopicNotAvailable, null);
    public static readonly SubmitMessageResult MakeDescriptionRequired = new(SubmitMessageOutcome.MakeDescriptionRequired, null);

    public static SubmitMessageResult Accept(SendMessageResponse response) => new(SubmitMessageOutcome.Accepted, response);
}

public enum SubmitMessageOutcome
{
    Accepted = 0,
    NotFound = 1,
    Conflict = 2,
    Forbidden = 3,
    ModelNotAvailable = 4,
    MakeTopicNotAvailable = 5,
    MakeDescriptionRequired = 6,
}
