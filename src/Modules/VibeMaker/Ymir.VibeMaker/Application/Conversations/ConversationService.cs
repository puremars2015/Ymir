using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Identity;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Contracts.Conversations;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Conversations;

/// <summary>Conversation / Message 用例（SA §5、§9）。只回傳目前使用者自己的資料（SA §12）。</summary>
public sealed class ConversationService(IVibeMakerDbContext db, ICurrentUser currentUser, TimeProvider timeProvider)
{
    /// <returns><paramref name="workspaceId"/> 指定了別人的 workspace 時回傳 null（→ 404）。</returns>
    public async Task<IReadOnlyList<ConversationResponse>?> ListAsync(Guid? workspaceId, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (workspaceId is { } id && !await db.Workspaces.AnyAsync(w => w.Id == id && w.UserId == userId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var query = db.Conversations.AsNoTracking().Where(c => c.UserId == userId && c.Status == ConversationStatus.Active);
        if (workspaceId is { } filter)
        {
            query = query.Where(c => c.WorkspaceId == filter);
        }

        var conversations = await query.OrderByDescending(c => c.UpdatedAt).ToListAsync(cancellationToken).ConfigureAwait(false);
        return conversations.Select(ToResponse).ToList();
    }

    /// <returns>workspace 不存在或不是自己的時回傳 null（→ 404）。</returns>
    public async Task<ConversationResponse?> CreateAsync(CreateConversationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = currentUser.UserId;
        var workspace = await db.Workspaces.AsNoTracking()
            .SingleOrDefaultAsync(w => w.Id == request.WorkspaceId && w.UserId == userId && w.Status == WorkspaceStatus.Active, cancellationToken)
            .ConfigureAwait(false);
        if (workspace is null)
        {
            return null;
        }

        var conversation = Conversation.Create(workspace, request.Title, timeProvider.GetUtcNow());
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

    private static ConversationResponse ToResponse(Conversation c) => new(c.Id, c.WorkspaceId, c.Title, SaValues.Of(c.Status), c.CreatedAt, c.UpdatedAt);
}
