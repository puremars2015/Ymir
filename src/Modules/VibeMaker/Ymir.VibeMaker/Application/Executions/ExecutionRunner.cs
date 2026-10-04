using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ymir.Platform.Auditing;
using Ymir.VibeMaker.Application.Agents;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Contracts.Executions;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Executions;

/// <summary>
/// 執行一個 QUEUED execution（SA §11 時序）：EnsureRuntime → AgentSession → Agent harness → 保存事件 / ASSISTANT 訊息 / 狀態。
/// 不論成功、失敗、取消或逾時，最後一定寫入終止事件並把狀態推進到終止狀態（SA §14），不會卡在 RUNNING。
/// </summary>
public sealed class ExecutionRunner(
    IVibeMakerDbContext db,
    IAgentRuntimeManager runtimeManager,
    IAgentHarness harness,
    ExecutionEventWriter eventWriter,
    IExecutionCancellationRegistry cancellations,
    WorkspaceExecutionLocks workspaceLocks,
    IOptions<ExecutionOptions> options,
    IAuditLog auditLog,
    TimeProvider timeProvider,
    ILogger<ExecutionRunner> logger)
{
    private long _sequence;

    public async Task RunAsync(Guid executionId, CancellationToken stoppingToken)
    {
        var execution = await db.AgentExecutions.SingleOrDefaultAsync(e => e.Id == executionId, stoppingToken).ConfigureAwait(false);
        if (execution is null || execution.Status != ExecutionStatus.Queued)
        {
            return; // 已被取消或由其他流程處理
        }

        _sequence = await db.ExecutionEvents.Where(e => e.ExecutionId == executionId)
            .MaxAsync(e => (long?)e.Sequence, stoppingToken).ConfigureAwait(false) ?? 0;

        using var workspaceLock = await workspaceLocks.AcquireAsync(execution.WorkspaceId, stoppingToken).ConfigureAwait(false);

        // 等待 lock 期間可能已被取消。
        await db.Entry(execution).ReloadAsync(stoppingToken).ConfigureAwait(false);
        if (execution.Status != ExecutionStatus.Queued)
        {
            return;
        }

        using var userCancellation = cancellations.Register(executionId);
        using var timeout = new CancellationTokenSource(options.Value.Timeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(userCancellation.Token, timeout.Token, stoppingToken);
        try
        {
            await RunRegisteredAsync(execution, timeout, linked.Token, stoppingToken).ConfigureAwait(false);
        }
        finally
        {
            cancellations.Unregister(executionId);
        }
    }

    private async Task RunRegisteredAsync(AgentExecution execution, CancellationTokenSource timeout, CancellationToken runToken, CancellationToken stoppingToken)
    {
        var executionId = execution.Id;
        RuntimeInfo runtime;
        AgentSession session;
        try
        {
            await AppendAsync(executionId, new StatusEvent("正在準備 Runtime"), stoppingToken).ConfigureAwait(false);
            runtime = await runtimeManager.EnsureRuntimeAsync(execution.WorkspaceId, runToken).ConfigureAwait(false);
            await RecordRuntimeAsync(runtime, stoppingToken).ConfigureAwait(false);
            session = await GetOrCreateSessionAsync(execution, runtime, stoppingToken).ConfigureAwait(false);

            execution.Start(session.Id, runtime.RuntimeId, timeProvider.GetUtcNow());
            await db.SaveChangesAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogInformation("Execution {ExecutionId} was cancelled before it started", executionId);
            return;
        }
        catch (OperationCanceledException) when (runToken.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            await FinishAsync(execution, timeout.IsCancellationRequested
                ? new AgentFailed(ExecutionErrorCodes.AgentTimeout, "執行超過時間限制。")
                : new AgentCancelled(string.Empty), stoppingToken).ConfigureAwait(false);
            return;
        }
#pragma warning disable CA1031 // runtime 啟動失敗必須轉成 execution.failed（SA §13 RUNTIME_START_FAILED），不能讓 execution 卡住。
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Failed to prepare runtime for execution {ExecutionId}", executionId);
            await FinishAsync(execution, new AgentFailed(ExecutionErrorCodes.RuntimeStartFailed, "無法啟動執行環境，請稍後再試。"), stoppingToken).ConfigureAwait(false);
            return;
        }

        await auditLog.WriteAsync(new AuditEntry("system", "execution.start", "execution", executionId.ToString("D"), AuditResult.Success, timeProvider.GetUtcNow(), null), stoppingToken)
            .ConfigureAwait(false);

        AgentEvent? terminal = null;
        try
        {
            var request = new AgentRunRequest(executionId, runtime.RuntimeId, session.Id, await GetPromptAsync(execution, stoppingToken).ConfigureAwait(false));
            await foreach (var agentEvent in harness.RunAsync(request, runToken).ConfigureAwait(false))
            {
                if (agentEvent is AgentCompleted or AgentFailed or AgentCancelled)
                {
                    terminal = agentEvent;
                    break;
                }

                await AppendAsync(executionId, agentEvent.ToExecutionEvent(executionId), stoppingToken).ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // 同上：任何例外都要結束 execution。
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Execution {ExecutionId} failed unexpectedly", executionId);
            terminal = new AgentFailed(ExecutionErrorCodes.AgentRuntimeError, "Agent 執行環境發生錯誤。");
        }

        // harness 保證以終止事件結束；逾時造成的取消轉為 AGENT_TIMEOUT（SA §13）。
        terminal ??= new AgentFailed(ExecutionErrorCodes.AgentRuntimeError, "Agent 沒有回傳結果。");
        if (terminal is AgentCancelled cancelled && timeout.IsCancellationRequested)
        {
            terminal = new AgentFailed(ExecutionErrorCodes.AgentTimeout, "執行超過時間限制。");
            await FinishAsync(execution, terminal, stoppingToken, cancelled.PartialText).ConfigureAwait(false);
            return;
        }

        await FinishAsync(execution, terminal, stoppingToken).ConfigureAwait(false);
    }

    /// <summary>保存 ASSISTANT 訊息、推進狀態並寫入終止事件。</summary>
    private async Task FinishAsync(AgentExecution execution, AgentEvent terminal, CancellationToken cancellationToken, string? partialText = null)
    {
        var now = timeProvider.GetUtcNow();
        var text = terminal switch
        {
            AgentCompleted completed => completed.FinalText,
            AgentCancelled cancelled => cancelled.PartialText,
            _ => partialText ?? string.Empty,
        };

        Message? assistantMessage = null;
        if (!string.IsNullOrEmpty(text))
        {
            assistantMessage = await AddAssistantMessageAsync(execution, text, MessageType.Text, now, cancellationToken).ConfigureAwait(false);
        }

        ExecutionEvent terminalEvent;
        switch (terminal)
        {
            case AgentCompleted:
                execution.Complete(assistantMessage?.Id, now);
                terminalEvent = new ExecutionCompletedEvent(execution.Id, assistantMessage?.Id);
                break;
            case AgentFailed failed:
                // 失敗原因也保存為 ERROR 訊息，重新整理後仍看得到（只有使用者可讀的摘要，SA §12）。
                var errorMessage = await AddAssistantMessageAsync(execution, failed.Message, MessageType.Error, now, cancellationToken).ConfigureAwait(false);
                execution.Fail(failed.Code, assistantMessage?.Id ?? errorMessage.Id, now);
                terminalEvent = new ExecutionFailedEvent(failed.Code, failed.Message);
                break;
            default:
                execution.Cancel(assistantMessage?.Id, now);
                terminalEvent = new ExecutionCancelledEvent(execution.Id);
                break;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await AppendAsync(execution.Id, terminalEvent, cancellationToken).ConfigureAwait(false);
        await auditLog.WriteAsync(new AuditEntry("system", "execution.finish", "execution", execution.Id.ToString("D"),
            execution.Status == ExecutionStatus.Completed ? AuditResult.Success : AuditResult.Failure, now, null), cancellationToken).ConfigureAwait(false);
    }

    private async Task<Message> AddAssistantMessageAsync(AgentExecution execution, string content, MessageType type, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var nextSequence = await db.Messages.Where(m => m.ConversationId == execution.ConversationId)
            .MaxAsync(m => (long?)m.SequenceNo, cancellationToken).ConfigureAwait(false) ?? 0;
        var message = Message.CreateAssistant(execution.ConversationId, execution.Id, content, type, nextSequence + 1, now);
        db.Messages.Add(message);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return message;
    }

    private Task<StoredExecutionEvent> AppendAsync(Guid executionId, ExecutionEvent executionEvent, CancellationToken cancellationToken) =>
        eventWriter.AppendAsync(executionId, ++_sequence, executionEvent, cancellationToken);

    private async Task<string> GetPromptAsync(AgentExecution execution, CancellationToken cancellationToken) =>
        await db.Messages.Where(m => m.Id == execution.UserMessageId).Select(m => m.Content).SingleAsync(cancellationToken).ConfigureAwait(false);

    private async Task RecordRuntimeAsync(RuntimeInfo runtime, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var record = await db.AgentRuntimes
            .SingleOrDefaultAsync(r => r.WorkspaceId == runtime.WorkspaceId && r.Status != RuntimeStatus.Deleted, cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            db.AgentRuntimes.Add(AgentRuntimeRecord.Create(runtime.RuntimeId, runtime.WorkspaceId, runtime.Provider, runtime.ProviderRuntimeId, runtime.ImageVersion, runtime.Status, now));
        }
        else
        {
            record.Update(runtime.ProviderRuntimeId, runtime.ImageVersion, runtime.Status, now);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<AgentSession> GetOrCreateSessionAsync(AgentExecution execution, RuntimeInfo runtime, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var session = await db.AgentSessions.SingleOrDefaultAsync(s => s.ConversationId == execution.ConversationId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            var conversation = await db.Conversations.SingleAsync(c => c.Id == execution.ConversationId, cancellationToken).ConfigureAwait(false);
            session = AgentSession.Create(conversation, now);
            db.AgentSessions.Add(session);
        }

        session.BindRuntime(runtime.RuntimeId, now);
        return session;
    }
}
