using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Auditing;
using Ymir.Platform.Identity;
using Ymir.VibeMaker.Application.Attachments;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Contracts.Conversations;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Conversations;

/// <summary>Conversation / Message 用例（SA §5、§9）。只回傳目前使用者自己的資料（SA §12）。</summary>
public sealed class ConversationService(IVibeMakerDbContext db, ICurrentUser currentUser, IAuditLog auditLog, TimeProvider timeProvider)
{
    /// <summary>目前使用者的對話；指定 <paramref name="projectId"/> 時只列該專案的對話（側邊欄由前端依 projectId 分組）。</summary>
    /// <returns><paramref name="projectId"/> 指定了別人的專案時回傳 null（→ 404）。</returns>
    public async Task<IReadOnlyList<ConversationResponse>?> ListAsync(Guid? projectId, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (projectId is { } id && !await db.Projects.AnyAsync(p => p.Id == id && p.UserId == userId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var query = db.Conversations.AsNoTracking().Where(c => c.UserId == userId && c.Status == ConversationStatus.Active);
        if (projectId is { } filter)
        {
            query = query.Where(c => c.ProjectId == filter);
        }

        var conversations = await query.OrderByDescending(c => c.UpdatedAt).ToListAsync(cancellationToken).ConfigureAwait(false);
        return conversations.Select(ToResponse).ToList();
    }

    /// <returns>指定的專案不存在或不是自己的時回傳 null（→ 404）；不指定專案則建立未分組的對話。</returns>
    public async Task<ConversationResponse?> CreateAsync(CreateConversationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = currentUser.UserId;
        var now = timeProvider.GetUtcNow();
        Project? project = null;
        if (request.ProjectId is { } projectId)
        {
            project = await db.Projects
                .SingleOrDefaultAsync(p => p.Id == projectId && p.UserId == userId && p.Status == ProjectStatus.Active, cancellationToken)
                .ConfigureAwait(false);
            if (project is null)
            {
                return null;
            }

            project.Touch(now); // 側邊欄依最近使用排序
        }

        var conversation = Conversation.Create(userId, project, request.Title, now);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToResponse(conversation);
    }

    public async Task<ConversationResponse?> GetAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await FindOwnedAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (conversation is null)
        {
            return null;
        }

        var active = await db.AgentExecutions.AsNoTracking()
            .Where(e => e.ConversationId == conversationId && (e.Status == ExecutionStatus.Queued || e.Status == ExecutionStatus.Running))
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return ToResponse(conversation) with { ActiveExecutionId = active };
    }

    /// <returns>對話不存在、已封存或不是自己的時回傳 null（→ 404）。</returns>
    public async Task<ConversationResponse?> RenameAsync(Guid conversationId, UpdateConversationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var conversation = await FindOwnedForUpdateAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (conversation is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        if (request.Title is not null)
        {
            conversation.Rename(request.Title, now);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditLog.WriteAsync(new AuditEntry(currentUser.ActorName, "conversation.update", "conversation", conversationId.ToString("D"), AuditResult.Success, now, null), cancellationToken)
            .ConfigureAwait(false);
        return ToResponse(conversation);
    }

    /// <summary>封存（「刪除」）對話；執行中的對話必須先停止。</summary>
    public async Task<ArchiveOutcome> ArchiveAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await FindOwnedForUpdateAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (conversation is null)
        {
            return ArchiveOutcome.NotFound;
        }

        if (await HasActiveExecutionAsync([conversationId], cancellationToken).ConfigureAwait(false))
        {
            return ArchiveOutcome.ExecutionInProgress;
        }

        var now = timeProvider.GetUtcNow();
        conversation.Archive(now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditLog.WriteAsync(new AuditEntry(currentUser.ActorName, "conversation.archive", "conversation", conversationId.ToString("D"), AuditResult.Success, now, null), cancellationToken)
            .ConfigureAwait(false);
        return ArchiveOutcome.Archived;
    }

    private Task<bool> HasActiveExecutionAsync(IReadOnlyCollection<Guid> conversationIds, CancellationToken cancellationToken) =>
        db.AgentExecutions.AnyAsync(e => conversationIds.Contains(e.ConversationId) && (e.Status == ExecutionStatus.Queued || e.Status == ExecutionStatus.Running), cancellationToken);

    private Task<Conversation?> FindOwnedForUpdateAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        return db.Conversations.SingleOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId && c.Status == ConversationStatus.Active, cancellationToken);
    }

    public async Task<IReadOnlyList<MessageResponse>?> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        if (await FindOwnedAsync(conversationId, cancellationToken).ConfigureAwait(false) is null)
        {
            return null;
        }

        var messages = await db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.SequenceNo)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var attachments = (await db.MessageAttachments.AsNoTracking()
                .Where(a => a.ConversationId == conversationId && a.MessageId != null)
                .OrderBy(a => a.CreatedAt)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToLookup(a => a.MessageId!.Value);
        return messages.Select(m => new MessageResponse(
            m.Id,
            SaValues.Of(m.Role),
            SaValues.Of(m.MessageType),
            m.Content,
            m.SequenceNo,
            m.ExecutionId,
            m.CreatedAt,
            [.. attachments[m.Id].Select(AttachmentService.ToResponse)])).ToList();
    }

    private Task<Conversation?> FindOwnedAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        // 封存（「刪除」）的對話對使用者而言已不存在（→ 404）。
        return db.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId && c.Status == ConversationStatus.Active, cancellationToken);
    }

    private static ConversationResponse ToResponse(Conversation c) => new(c.Id, c.ProjectId, c.Title, c.ModelId, SaValues.Of(c.Status), c.CreatedAt, c.UpdatedAt);
}

public enum ArchiveOutcome
{
    Archived,
    NotFound,
    ExecutionInProgress,
}
