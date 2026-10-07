using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ymir.Platform.Auditing;
using Ymir.VibeMaker.Application.Agents;
using Ymir.VibeMaker.Application.Models;
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
    RuntimeCredentialService credentials,
    ModelCatalog models,
    ExecutionEventWriter eventWriter,
    IExecutionCancellationRegistry cancellations,
    UserExecutionLocks userLocks,
    RuntimePolicyService policies,
    IAuditLog auditLog,
    VibeMakerTelemetry telemetry,
    TimeProvider timeProvider,
    ILogger<ExecutionRunner> logger)
{
    private long _sequence;
    private bool _started;

    public async Task RunAsync(Guid executionId, CancellationToken stoppingToken)
    {
        var execution = await db.AgentExecutions.SingleOrDefaultAsync(e => e.Id == executionId, stoppingToken).ConfigureAwait(false);
        if (execution is null || execution.Status != ExecutionStatus.Queued)
        {
            return; // 已被取消或由其他流程處理
        }

        _sequence = await db.ExecutionEvents.Where(e => e.ExecutionId == executionId)
            .MaxAsync(e => (long?)e.Sequence, stoppingToken).ConfigureAwait(false) ?? 0;

        // AC-10：同一次執行的 log 都帶 execution / conversation / user id，稽核的 correlation id 是這個 activity 的 trace id。
        using var activity = VibeMakerTelemetry.ActivitySource.StartActivity("vibemaker.execution");
        activity?.SetTag("ymir.execution_id", executionId.ToString("D"));
        using var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["ExecutionId"] = executionId,
            ["ConversationId"] = execution.ConversationId,
            ["UserId"] = execution.UserId,
        });

        // 一個使用者一個 container（ADR-0007）：同一使用者的 execution 依序執行。
        using var userLock = await userLocks.AcquireAsync(execution.UserId, stoppingToken).ConfigureAwait(false);

        // 等待 lock 期間可能已被取消。
        await db.Entry(execution).ReloadAsync(stoppingToken).ConfigureAwait(false);
        if (execution.Status != ExecutionStatus.Queued)
        {
            return;
        }

        using var userCancellation = cancellations.Register(executionId);
        // 逾時以目前的執行政策為準（管理介面可調整，ADR-0011）。
        var policy = await policies.GetAsync(stoppingToken).ConfigureAwait(false);
        using var timeout = new CancellationTokenSource(policy.ExecutionTimeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(userCancellation.Token, timeout.Token, stoppingToken);
        try
        {
            await RunRegisteredAsync(execution, policy, timeout, linked.Token, stoppingToken).ConfigureAwait(false);
        }
        finally
        {
            cancellations.Unregister(executionId);
        }
    }

    private async Task RunRegisteredAsync(AgentExecution execution, RuntimePolicy policy, CancellationTokenSource timeout, CancellationToken runToken, CancellationToken stoppingToken)
    {
        var executionId = execution.Id;
        RuntimeInfo runtime;
        AgentSession session;
        RuntimeModelCredential credential;
        try
        {
            await AppendAsync(executionId, new StatusEvent("正在準備 Runtime"), stoppingToken).ConfigureAwait(false);
            runtime = await runtimeManager.EnsureRuntimeAsync(execution.UserId, runToken).ConfigureAwait(false);
            await RecordRuntimeAsync(runtime, stoppingToken).ConfigureAwait(false);
            await AuditRuntimeTransitionAsync(runtime, stoppingToken).ConfigureAwait(false);
            session = await GetOrCreateSessionAsync(execution, runtime, stoppingToken).ConfigureAwait(false);
            // 使用者的 LiteLLM virtual key（ADR-0004）：只放進 Agent 程序的環境變數，container 內不會有 master key。
            credential = await credentials.GetAsync(execution.UserId, runtime.RuntimeId, runToken, policy.MonthlyBudget).ConfigureAwait(false);

            execution.Start(session.Id, runtime.RuntimeId, timeProvider.GetUtcNow());
            await db.SaveChangesAsync(stoppingToken).ConfigureAwait(false);
            _started = true;
            telemetry.ExecutionStarted();
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
        catch (ModelCredentialException ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Failed to obtain model credential for execution {ExecutionId}", executionId);
            await FinishAsync(execution, new AgentFailed(ExecutionErrorCodes.ModelProviderError, "暫時無法連線到模型服務，請稍後再試。"), stoppingToken).ConfigureAwait(false);
            return;
        }
#pragma warning disable CA1031 // runtime 啟動失敗必須轉成 execution.failed（SA §13 RUNTIME_START_FAILED），不能讓 execution 卡住。
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Failed to prepare runtime for execution {ExecutionId}", executionId);
            telemetry.RuntimeStartFailed();
            await auditLog.WriteAsync(
                new AuditEntry("system", "runtime.ensure", "user", execution.UserId.ToString("D"), AuditResult.Failure, timeProvider.GetUtcNow(), null),
                stoppingToken).ConfigureAwait(false);
            await FinishAsync(execution, new AgentFailed(ExecutionErrorCodes.RuntimeStartFailed, "無法啟動執行環境，請稍後再試。"), stoppingToken).ConfigureAwait(false);
            return;
        }

        await auditLog.WriteAsync(new AuditEntry("system", "execution.start", "execution", executionId.ToString("D"), AuditResult.Success, timeProvider.GetUtcNow(), null), stoppingToken)
            .ConfigureAwait(false);

        AgentEvent? terminal = null;
        try
        {
            var request = new AgentRunRequest(
                executionId,
                runtime.RuntimeId,
                session.Id,
                await GetPromptAsync(execution, stoppingToken).ConfigureAwait(false),
                await GetWorkingDirectoryAsync(execution, stoppingToken).ConfigureAwait(false),
                credential.ApiKey,
                models.Resolve(execution.ModelId),
                await GetSystemPromptsAsync(execution, stoppingToken).ConfigureAwait(false));
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

        // 執行結束也算使用者活動：閒置停止從最後一次 execution 結束開始計時。
        var runtimeRecord = await db.AgentRuntimes
            .FirstOrDefaultAsync(r => r.UserId == execution.UserId && r.Status != RuntimeStatus.Deleted, cancellationToken)
            .ConfigureAwait(false);
        runtimeRecord?.MarkActive(now);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await AppendAsync(execution.Id, terminalEvent, cancellationToken).ConfigureAwait(false);
        telemetry.ExecutionFinished(
            _started,
            execution.Status.ToString(),
            execution.ErrorCode,
            execution.StartedAt is { } startedAt && execution.EndedAt is { } endedAt ? endedAt - startedAt : null);
        _started = false;
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

    /// <summary>送給 Agent 的內容：<c>/make</c> 展開後的指示，否則是 USER 訊息原文。</summary>
    private async Task<string> GetPromptAsync(AgentExecution execution, CancellationToken cancellationToken) =>
        execution.AgentPrompt
        ?? await db.Messages.Where(m => m.Id == execution.UserMessageId).Select(m => m.Content).SingleAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>有專案的對話在專案目錄工作（共用檔案），未分組的對話在自己的目錄工作（ADR-0007）。</summary>
    private async Task<string> GetWorkingDirectoryAsync(AgentExecution execution, CancellationToken cancellationToken)
    {
        var projectId = await db.Conversations.Where(c => c.Id == execution.ConversationId).Select(c => c.ProjectId).SingleAsync(cancellationToken).ConfigureAwait(false);
        return RuntimePaths.WorkingDirectoryFor(execution.ConversationId, projectId);
    }

    /// <summary>個人 global system prompt 在前、專案 system prompt 在後；未設定的省略。</summary>
    private async Task<IReadOnlyList<string>> GetSystemPromptsAsync(AgentExecution execution, CancellationToken cancellationToken)
    {
        var prompts = new List<string>(2);
        var userPrompt = await db.UserSettings.Where(s => s.UserId == execution.UserId).Select(s => s.SystemPrompt).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(userPrompt))
        {
            prompts.Add(userPrompt);
        }

        var projectPrompt = await db.Conversations.Where(c => c.Id == execution.ConversationId && c.ProjectId != null)
            .Join(db.Projects, c => c.ProjectId, p => p.Id, (_, p) => p.SystemPrompt)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(projectPrompt))
        {
            prompts.Add(projectPrompt);
        }

        return prompts;
    }

    /// <summary>SA §12：runtime 的建立與啟動寫入稽核（原本就在執行時不寫，避免每次 execution 都產生一筆）。</summary>
    private async Task AuditRuntimeTransitionAsync(RuntimeInfo runtime, CancellationToken cancellationToken)
    {
        var action = runtime.Transition switch
        {
            RuntimeTransition.Created => "runtime.create",
            RuntimeTransition.Started => "runtime.start",
            _ => null,
        };
        if (action is not null)
        {
            await auditLog.WriteAsync(
                new AuditEntry("system", action, "user", runtime.UserId.ToString("D"), AuditResult.Success, timeProvider.GetUtcNow(), null),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RecordRuntimeAsync(RuntimeInfo runtime, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var record = await db.AgentRuntimes
            .SingleOrDefaultAsync(r => r.UserId == runtime.UserId && r.Status != RuntimeStatus.Deleted, cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            db.AgentRuntimes.Add(AgentRuntimeRecord.Create(runtime.RuntimeId, runtime.UserId, runtime.Provider, runtime.ProviderRuntimeId, runtime.ImageVersion, runtime.Status, now));
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
