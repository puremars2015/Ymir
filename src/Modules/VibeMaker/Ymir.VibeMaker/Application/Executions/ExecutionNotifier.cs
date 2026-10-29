using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ymir.Platform.Notifications;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Executions;

/// <summary>
/// 長任務完成通知（ADR-0032 §3，N1）：execution 從送出到結束超過門檻
/// （<c>VibeMaker:Notifications:LongExecutionSeconds</c>，預設 60 秒）時，完成或失敗都通知 execution 的使用者。
/// <list type="bullet">
/// <item>使用者取消的不通知。</item>
/// <item>群組 @bot（ADR-0026）只通知呼叫者；連結為 null（群組對話不屬於任何人的對話清單），回覆仍在群組聊天裡。</item>
/// <item>best effort：失敗只寫 log，不影響 execution 的狀態與終止事件。</item>
/// </list>
/// 內容只有摘要（SA §10、§12）：標題用對話標題，失敗原因沿用已經給使用者看的摘要；連結只由伺服器產生。
/// </summary>
public sealed class ExecutionNotifier(
    IVibeMakerDbContext db,
    INotificationService notifications,
    IOptions<ExecutionOptions> options,
    ILogger<ExecutionNotifier> logger)
{
    /// <param name="failureMessage">失敗時給使用者看的摘要；完成時為 null。</param>
    public async Task NotifyFinishedAsync(AgentExecution execution, string? failureMessage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);
        if (execution.Status is not (ExecutionStatus.Completed or ExecutionStatus.Failed) || execution.EndedAt is not { } endedAt)
        {
            return;
        }

        // 從送出算起：排隊等待也是使用者等待的時間。
        var elapsed = endedAt - execution.CreatedAt;
        if (elapsed < options.Value.LongExecutionNotificationThreshold)
        {
            return;
        }

        try
        {
            var conversation = await db.Conversations.AsNoTracking()
                .Where(c => c.Id == execution.ConversationId)
                .Select(c => new { c.UserId, c.Title })
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (conversation is null)
            {
                return;
            }

            var personal = conversation.UserId == execution.UserId;
            var subject = personal ? conversation.Title : "群組 @bot";
            var completed = execution.Status == ExecutionStatus.Completed;
            var title = completed ? $"已完成：{subject}" : $"執行失敗：{subject}";
            var body = completed
                ? $"Agent 花了 {FormatDuration(elapsed)}完成這次工作。" + (personal ? string.Empty : "回覆已在群組聊天中。")
                : failureMessage;
            await notifications.CreateAsync(
                execution.UserId,
                completed ? NotificationKind.ExecutionCompleted : NotificationKind.ExecutionFailed,
                title,
                body,
                personal ? $"/c/{execution.ConversationId:D}" : null,
                execution.Id,
                cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // 通知是 best effort：execution 已經結束，通知失敗不能改變它的結果。
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Failed to notify the user about execution {ExecutionId}", execution.Id);
        }
    }

    internal static string FormatDuration(TimeSpan elapsed) =>
        elapsed.TotalMinutes >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalMinutes} 分 {elapsed.Seconds} 秒")
            : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, (int)elapsed.TotalSeconds)} 秒");
}
