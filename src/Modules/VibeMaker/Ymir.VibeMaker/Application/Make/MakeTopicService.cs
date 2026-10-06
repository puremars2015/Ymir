using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Auditing;
using Ymir.Platform.Identity;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Contracts.Make;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Make;

/// <summary><c>/make</c> 主題：一般使用者只看得到啟用中的主題名稱與說明；新增 / 修改 / 刪除只限 Admin（端點以 AdminPolicy 保護）。</summary>
public sealed class MakeTopicService(IVibeMakerDbContext db, ICurrentUser currentUser, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<MakeTopicResponse>> ListEnabledAsync(CancellationToken cancellationToken) =>
        await db.MakeTopics.AsNoTracking()
            .Where(t => t.IsEnabled)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .Select(t => new MakeTopicResponse(t.Id, t.Name, t.Description, t.SortOrder))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>組合 <c>/make</c> prompt 用：啟用中的主題（含建置指示）。</summary>
    public async Task<IReadOnlyList<MakeTopicPrompt>> ListEnabledPromptsAsync(CancellationToken cancellationToken) =>
        await db.MakeTopics.AsNoTracking()
            .Where(t => t.IsEnabled)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .Select(t => new MakeTopicPrompt(t.Name, t.Description, t.Instructions))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<MakeTopicPrompt?> FindEnabledPromptAsync(Guid topicId, CancellationToken cancellationToken) =>
        await db.MakeTopics.AsNoTracking()
            .Where(t => t.Id == topicId && t.IsEnabled)
            .Select(t => new MakeTopicPrompt(t.Name, t.Description, t.Instructions))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<AdminMakeTopicResponse>> ListAllAsync(CancellationToken cancellationToken)
    {
        var topics = await db.MakeTopics.AsNoTracking()
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. topics.Select(ToAdmin)];
    }

    public async Task<AdminMakeTopicResponse> CreateAsync(SaveMakeTopicRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = timeProvider.GetUtcNow();
        var topic = MakeTopic.Create(request.Name, request.Description, request.Instructions, request.SortOrder, request.IsEnabled, now);
        db.MakeTopics.Add(topic);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await AuditAsync("admin.make_topic.create", topic.Id, now, cancellationToken).ConfigureAwait(false);
        return ToAdmin(topic);
    }

    public async Task<AdminMakeTopicResponse?> UpdateAsync(Guid topicId, SaveMakeTopicRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var topic = await db.MakeTopics.SingleOrDefaultAsync(t => t.Id == topicId, cancellationToken).ConfigureAwait(false);
        if (topic is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        topic.Update(request.Name, request.Description, request.Instructions, request.SortOrder, request.IsEnabled, now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await AuditAsync("admin.make_topic.update", topic.Id, now, cancellationToken).ConfigureAwait(false);
        return ToAdmin(topic);
    }

    public async Task<bool> DeleteAsync(Guid topicId, CancellationToken cancellationToken)
    {
        var topic = await db.MakeTopics.SingleOrDefaultAsync(t => t.Id == topicId, cancellationToken).ConfigureAwait(false);
        if (topic is null)
        {
            return false;
        }

        db.MakeTopics.Remove(topic);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await AuditAsync("admin.make_topic.delete", topicId, timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        return true;
    }

    private Task AuditAsync(string action, Guid topicId, DateTimeOffset now, CancellationToken cancellationToken) =>
        auditLog.WriteAsync(new AuditEntry(currentUser.ActorName, action, "make_topic", topicId.ToString("D"), AuditResult.Success, now, null), cancellationToken);

    private static AdminMakeTopicResponse ToAdmin(MakeTopic topic) =>
        new(topic.Id, topic.Name, topic.Description, topic.Instructions, topic.SortOrder, topic.IsEnabled, topic.UpdatedAt);
}
