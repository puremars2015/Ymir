using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Identity;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Contracts.Conversations;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Conversations;

/// <summary>Conversation / Message 用例（SA §5、§9）。只回傳目前使用者自己的資料（SA §12）。</summary>
public sealed class ConversationService(IVibeMakerDbContext db, ICurrentUser currentUser, TimeProvider timeProvider)
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
        return conversation is null ? null : ToResponse(conversation);
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
        return messages.Select(m => new MessageResponse(m.Id, SaValues.Of(m.Role), SaValues.Of(m.MessageType), m.Content, m.SequenceNo, m.ExecutionId, m.CreatedAt)).ToList();
    }

    private Task<Conversation?> FindOwnedAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        return db.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId, cancellationToken);
    }

    private static ConversationResponse ToResponse(Conversation c) => new(c.Id, c.ProjectId, c.Title, c.ModelId, SaValues.Of(c.Status), c.CreatedAt, c.UpdatedAt);
}
