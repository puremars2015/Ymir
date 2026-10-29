using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Auditing;
using Ymir.Platform.Identity;
using Ymir.VibeMaker.Application.Models;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Schedules;

public sealed record SaveScheduledTask(Guid ConversationId, ScheduleKind Kind, string Prompt, string? ModelId,
    string? ThinkingLevel, string? CronExpression, string TimeZone, DateTimeOffset? RunAt);
public sealed record ScheduledTaskResponse(Guid Id, Guid ConversationId, ScheduleKind Kind, string Prompt,
    string? ModelId, string? ThinkingLevel, string? CronExpression, string TimeZone, ScheduleStatus Status,
    DateTimeOffset? NextRunAt, DateTimeOffset? LastRunAt, string? LastResult, int ConsecutiveFailures, string Version)
{
    public static ScheduledTaskResponse From(ScheduledTask t) => new(t.Id, t.ConversationId, t.Kind, t.Prompt,
        t.ModelId, t.ThinkingLevel, t.CronExpression, t.TimeZone, t.Status, t.NextRunAt, t.LastRunAt,
        t.LastResult, t.ConsecutiveFailures, Convert.ToBase64String(t.Version));
}

public sealed class ScheduledTaskService(IVibeMakerDbContext db, ICurrentUser user, RuntimePolicyService policies,
    ModelAccessService models, IAuditLog audit, TimeProvider clock)
{
    public async Task<IReadOnlyList<ScheduledTaskResponse>> ListAsync(CancellationToken ct) =>
        (await db.ScheduledTasks.AsNoTracking().Where(t => t.UserId == user.UserId)
            .OrderByDescending(t => t.CreatedAt).Take(500).ToListAsync(ct).ConfigureAwait(false))
        .Select(ScheduledTaskResponse.From).ToList();

    public async Task<ScheduledTaskResponse?> SaveAsync(Guid? id, SaveScheduledTask request, string? version, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Serializable 範圍鎖防止同一人並行新增或恢復而超過上限（ADR-0032）。
        await using var transaction = await db.BeginSerializableTransactionAsync(ct).ConfigureAwait(false);
        var existing = id is null ? null : await FindAsync(id.Value, version, ct).ConfigureAwait(false);
        if (id is not null && existing is null) return null;
        if (!await db.Conversations.AnyAsync(c => c.Id == request.ConversationId && c.UserId == user.UserId && c.Status == ConversationStatus.Active, ct).ConfigureAwait(false)) return null;
        if (existing is not null && existing.ConversationId != request.ConversationId) throw new DomainValidationException("不能變更排程的對話。");
        if (!Enum.IsDefined(request.Kind)) throw new DomainValidationException("排程種類不正確。");
        var policy = await policies.GetAsync(ct).ConfigureAwait(false);
        if (existing is null) await CheckLimitAsync(policy, ct).ConfigureAwait(false);
        if (request.Kind == ScheduleKind.AgentRun)
        {
            var available = await models.GetForUserAsync(user.UserId, ct).ConfigureAwait(false);
            if (request.ModelId is null || !available.Models.Any(m => m.Id == request.ModelId && m.AcceptsThinking(request.ThinkingLevel)))
                throw new DomainValidationException("請選擇可用的模型與思考深度。");
        }
        var next = ScheduleRules.Validate(request.CronExpression, request.TimeZone, request.RunAt, clock.GetUtcNow(), policy.MinimumScheduleIntervalMinutes);
        var draft = ScheduledTask.Create(user.UserId, request.ConversationId, request.Kind, request.Prompt,
            request.Kind == ScheduleKind.AgentRun ? request.ModelId : null, request.ThinkingLevel,
            request.CronExpression, request.TimeZone, next, clock.GetUtcNow());
        if (existing is null) db.ScheduledTasks.Add(draft); else existing.Replace(draft);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        var task = existing ?? draft;
        await AuditAsync(task.Id, id is null ? "create" : "update", ct).ConfigureAwait(false);
        return ScheduledTaskResponse.From(task);
    }

    public async Task<ScheduledTaskResponse?> ChangeAsync(Guid id, string action, string version, CancellationToken ct)
    {
        await using var transaction = await db.BeginSerializableTransactionAsync(ct).ConfigureAwait(false);
        var task = await FindAsync(id, version, ct).ConfigureAwait(false);
        if (task is null) return null;
        switch (action)
        {
            case "pause": task.Pause(); break;
            case "delete": task.End(); break;
            case "confirm":
            case "resume":
                if (task.Status == ScheduleStatus.Ended) throw new DomainValidationException("已結束的排程不可恢復，請建立新排程。");
                if (task.Status != ScheduleStatus.Active)
                {
                    await CheckLimitAsync(await policies.GetAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                    if (!await db.Conversations.AnyAsync(c => c.Id == task.ConversationId && c.UserId == user.UserId && c.Status == ConversationStatus.Active, ct).ConfigureAwait(false)) return null;
                    var next = task.CronExpression is null ? task.NextRunAt : ScheduleRules.Next(task.CronExpression, task.TimeZone, clock.GetUtcNow());
                    if (next is null || next <= clock.GetUtcNow()) throw new DomainValidationException("排程時間已過，請先編輯時間。");
                    task.Resume(next.Value);
                }
                break;
            default: throw new DomainValidationException("無效的排程操作。");
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        await AuditAsync(id, action, ct).ConfigureAwait(false);
        return ScheduledTaskResponse.From(task);
    }
    private async Task<ScheduledTask?> FindAsync(Guid id, string? version, CancellationToken ct)
    {
        var task = await db.ScheduledTasks.SingleOrDefaultAsync(t => t.Id == id && t.UserId == user.UserId, ct).ConfigureAwait(false);
        if (task is null) return null;
        if (version != Convert.ToBase64String(task.Version) || task.LeaseUntil > clock.GetUtcNow())
            throw new DbUpdateConcurrencyException("排程已更新或正在觸發，請重新整理。");
        if (task.Status == ScheduleStatus.Ended) throw new DomainValidationException("排程已結束。");
        return task;
    }
    private async Task CheckLimitAsync(RuntimePolicy policy, CancellationToken ct)
    {
        if (await db.ScheduledTasks.CountAsync(t => t.UserId == user.UserId && (t.Status == ScheduleStatus.Active || t.Status == ScheduleStatus.PendingConfirmation), ct).ConfigureAwait(false) >= policy.MaxScheduledTasksPerUser)
            throw new DomainValidationException($"有效排程已達 {policy.MaxScheduledTasksPerUser} 個上限。");
    }
    private Task AuditAsync(Guid id, string action, CancellationToken ct) => audit.WriteAsync(
        new AuditEntry(user.ActorName, "schedule." + action, "schedule", id.ToString("D"), AuditResult.Success, clock.GetUtcNow(), null), ct);
}
