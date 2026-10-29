using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Notifications;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Application.Executions;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Contracts.Conversations;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Schedules;

public sealed class ScheduledTaskProcessor(IVibeMakerDbContext db, IUserDirectory users, ExecutionService executions,
    INotificationService notifications, RuntimePolicyService policies, TimeProvider clock)
{
    public async Task RunAsync(Guid id, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var task = await db.ScheduledTasks.SingleOrDefaultAsync(t => t.Id == id && t.Status == ScheduleStatus.Active
            && t.NextRunAt <= now && (t.LeaseUntil == null || t.LeaseUntil <= now), ct).ConfigureAwait(false);
        if (task is null) return;
        task.Claim(now);
        try { await db.SaveChangesAsync(ct).ConfigureAwait(false); }
        catch (DbUpdateConcurrencyException) { return; }
        var owner = await users.FindAsync(task.UserId, ct).ConfigureAwait(false);
        if (owner?.Status != UserStatus.Active || !await db.Conversations.AnyAsync(c => c.Id == task.ConversationId
            && c.UserId == task.UserId && c.Status == ConversationStatus.Active, ct).ConfigureAwait(false))
        {
            task.End(); await db.SaveChangesAsync(ct).ConfigureAwait(false); return;
        }
        // 前一輪可能在輪詢之後才結束；先收斂結果，避免新 execution 覆蓋尚未記錄的失敗。
        var previousBusy = false;
        if (task.LastExecutionId is { } previousId)
        {
            var previous = await db.AgentExecutions.Where(e => e.Id == previousId).Select(e => (ExecutionStatus?)e.Status).SingleOrDefaultAsync(ct).ConfigureAwait(false);
            if (previous is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled)
            {
                task.RecordOutcome(previous.Value);
                if (task.Status == ScheduleStatus.Paused)
                {
                    task.ReleaseLease();
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    await notifications.CreateAsync(task.UserId, NotificationKind.ScheduleFailed, "排程已暫停",
                        "Agent 工作連續失敗三次，請檢查後再恢復排程。", "/settings/schedules", previousId, ct).ConfigureAwait(false);
                    return;
                }
            }
            else previousBusy = true;
        }
        var occurrence = OccurrenceId(task.Id, task.NextRunAt!.Value);
        var failed = false;
        var result = "提醒已送達";
        if (task.Kind == ScheduleKind.Reminder)
        {
            await notifications.CreateAsync(task.UserId, NotificationKind.Reminder, "排程提醒", task.Prompt,
                $"/c/{task.ConversationId:D}", occurrence, ct).ConfigureAwait(false);
        }
        else if (previousBusy)
        {
            result = "略過：對話忙碌中";
        }
        else
        {
            SubmitMessageResult submitted;
            try
            {
                submitted = await executions.SubmitScheduledAsync(task.UserId, task.ConversationId,
                    new SendMessageRequest(task.Prompt, occurrence, task.ModelId, ThinkingLevel: task.ThinkingLevel), ct).ConfigureAwait(false);
            }
            catch (DomainValidationException) { submitted = SubmitMessageResult.ModelNotAvailable; }
            failed = submitted.Outcome is not (SubmitMessageOutcome.Accepted or SubmitMessageOutcome.Conflict);
            result = submitted.Outcome switch
            {
                SubmitMessageOutcome.Accepted => "已提交 Agent 工作",
                SubmitMessageOutcome.Conflict => "略過：對話忙碌中",
                SubmitMessageOutcome.ModelNotAvailable => "失敗：模型或思考深度不可用",
                SubmitMessageOutcome.QuotaExceeded => "失敗：配額或預算不足",
                _ => "失敗：目前無法建立工作",
            };
            // Submit 的唯一索引衝突會清除追蹤；重新讀取本輪租約。
            task = await db.ScheduledTasks.SingleAsync(t => t.Id == id, ct).ConfigureAwait(false);
            if (submitted.Response is { } accepted) task.TrackExecution(accepted.ExecutionId);
        }
        var policy = await policies.GetAsync(ct).ConfigureAwait(false);
        var earliest = task.NextRunAt!.Value.AddMinutes(policy.MinimumScheduleIntervalMinutes).AddTicks(-1);
        var next = task.CronExpression is null ? null : ScheduleRules.Next(task.CronExpression, task.TimeZone,
            earliest > now ? earliest : now);
        task.Finish(now, next, result, failed, resetFailures: task.Kind == ScheduleKind.Reminder);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (failed)
            await notifications.CreateAsync(task.UserId, NotificationKind.ScheduleFailed, "排程執行失敗",
                result + (task.Status == ScheduleStatus.Paused ? "；連續失敗三次，排程已暫停。" : string.Empty),
                "/settings/schedules", occurrence, ct).ConfigureAwait(false);
    }

    public async Task ReconcileResultsAsync(CancellationToken ct)
    {
        var tasks = await db.ScheduledTasks.Where(t => t.LastExecutionId != null).ToListAsync(ct).ConfigureAwait(false);
        foreach (var task in tasks)
        {
            var status = await db.AgentExecutions.Where(e => e.Id == task.LastExecutionId).Select(e => (ExecutionStatus?)e.Status).SingleOrDefaultAsync(ct).ConfigureAwait(false);
            if (status is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled)
            {
                var source = task.LastExecutionId;
                task.RecordOutcome(status.Value);
                try { await db.SaveChangesAsync(ct).ConfigureAwait(false); }
                catch (DbUpdateConcurrencyException) { db.Entry(task).State = EntityState.Detached; continue; }
                if (status == ExecutionStatus.Failed && task.Status == ScheduleStatus.Paused)
                    await notifications.CreateAsync(task.UserId, NotificationKind.ScheduleFailed, "排程已暫停",
                        "Agent 工作連續失敗三次，請檢查後再恢復排程。", "/settings/schedules", source, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>RFC 9562 UUID v5：相同 task 與預定 UTC 時間重啟後仍是同一輪。</summary>
    public static Guid OccurrenceId(Guid taskId, DateTimeOffset due)
    {
        var bytes = taskId.ToByteArray(bigEndian: true).Concat(Encoding.UTF8.GetBytes(due.UtcTicks.ToString(CultureInfo.InvariantCulture))).ToArray();
#pragma warning disable CA5350 // UUID v5 的標準演算法，僅為冪等識別碼，不用於密碼或授權。
        var hash = SHA1.HashData(bytes);
#pragma warning restore CA5350
        hash[6] = (byte)((hash[6] & 0x0f) | 0x50); hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }
}
