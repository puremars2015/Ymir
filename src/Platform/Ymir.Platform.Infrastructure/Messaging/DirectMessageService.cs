using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Infrastructure.Persistence;
using Ymir.Platform.Messaging;
using Ymir.Platform.Users;

namespace Ymir.Platform.Infrastructure.Messaging;

internal sealed class DirectMessageService(PlatformDbContext db, IDirectMessageHub hub, TimeProvider timeProvider) : IDirectMessageService
{
    /// <summary>名單一次最多列出的使用者數；有往來紀錄的人不受此限，一定會出現。</summary>
    internal const int MaxDirectoryUsers = 500;

    internal const int MaxPageSize = 100;

    public async Task<IReadOnlyList<DirectContact>> ListContactsAsync(Guid userId, string? search, CancellationToken cancellationToken)
    {
        // 未讀數與最後往來時間：分成「收到」與「送出」兩個 GroupBy，在記憶體合併，避免依 CASE 分組的複雜 SQL。
        var received = await db.DirectMessages.AsNoTracking()
            .Where(m => m.RecipientId == userId)
            .GroupBy(m => m.SenderId)
            .Select(g => new { PeerId = g.Key, Unread = g.Count(m => m.ReadAt == null), Last = g.Max(m => m.CreatedAt) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var sent = await db.DirectMessages.AsNoTracking()
            .Where(m => m.SenderId == userId)
            .GroupBy(m => m.RecipientId)
            .Select(g => new { PeerId = g.Key, Last = g.Max(m => m.CreatedAt) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var unread = received.ToDictionary(r => r.PeerId, r => r.Unread);
        var last = new Dictionary<Guid, DateTimeOffset>();
        foreach (var (peerId, at) in received.Select(r => (r.PeerId, r.Last)).Concat(sent.Select(s => (s.PeerId, s.Last))))
        {
            if (!last.TryGetValue(peerId, out var existing) || at > existing)
            {
                last[peerId] = at;
            }
        }

        var query = db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active && u.Id != userId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(u => u.DisplayName.Contains(term) || (u.AccountName != null && u.AccountName.Contains(term)) || (u.Department != null && u.Department.Contains(term)));
        }

        var users = await query.OrderBy(u => u.DisplayName).Take(MaxDirectoryUsers).ToListAsync(cancellationToken).ConfigureAwait(false);
        var missingPeers = last.Keys.Except(users.Select(u => u.Id)).ToList();
        if (missingPeers.Count > 0)
        {
            users.AddRange(await query.Where(u => missingPeers.Contains(u.Id)).ToListAsync(cancellationToken).ConfigureAwait(false));
        }

        return [.. users
            .Select(u => new DirectContact(u.Id, u.DisplayName, u.AccountName, u.Department, unread.GetValueOrDefault(u.Id), last.TryGetValue(u.Id, out var at) ? at : null))
            .OrderByDescending(c => c.UnreadCount > 0)
            .ThenByDescending(c => c.LastMessageAt)
            .ThenBy(c => c.DisplayName, StringComparer.CurrentCulture)];
    }

    public async Task<DirectMessagePage?> GetHistoryAsync(Guid userId, Guid peerId, Guid? before, int take, CancellationToken cancellationToken)
    {
        if (peerId == userId || !await db.Users.AsNoTracking().AnyAsync(u => u.Id == peerId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var query = Between(userId, peerId);
        if (before is { } cursor)
        {
            // 游標只在這兩人之間查；別人的訊息 id 查不到，等同沒有游標以外的訊息。
            var cursorAt = await Between(userId, peerId).Where(m => m.Id == cursor).Select(m => (DateTimeOffset?)m.CreatedAt)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (cursorAt is null)
            {
                return new DirectMessagePage([], false);
            }

            // SQL Server 的 uniqueidentifier 排序與 Guid v7 的時間順序不同，所以用 CreatedAt 當游標。
            query = query.Where(m => m.CreatedAt < cursorAt.Value);
        }

        var size = Math.Clamp(take, 1, MaxPageSize);
        var page = await query.OrderByDescending(m => m.CreatedAt).Take(size + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        var hasMore = page.Count > size;
        return new DirectMessagePage([.. page.Take(size).Reverse().Select(DirectMessageItem.From)], hasMore);
    }

    public async Task<DirectMessageSendResult> SendAsync(Guid senderId, Guid recipientId, string body, CancellationToken cancellationToken)
    {
        if (DirectMessage.NormalizeBody(body) is null)
        {
            return new(DirectMessageSendOutcome.InvalidBody, null);
        }

        if (recipientId == senderId
            || !await db.Users.AsNoTracking().AnyAsync(u => u.Id == recipientId && u.Status == UserStatus.Active, cancellationToken).ConfigureAwait(false))
        {
            return new(DirectMessageSendOutcome.RecipientNotFound, null);
        }

        var message = DirectMessage.Create(senderId, recipientId, body, timeProvider.GetUtcNow());
        db.DirectMessages.Add(message);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var item = DirectMessageItem.From(message);
        var notification = new DirectMessageSentNotification(item);
        hub.Publish(recipientId, notification);
        hub.Publish(senderId, notification);
        return new(DirectMessageSendOutcome.Sent, item);
    }

    public async Task<DirectMessagesRead?> MarkReadAsync(Guid userId, Guid peerId, CancellationToken cancellationToken)
    {
        if (peerId == userId || !await db.Users.AsNoTracking().AnyAsync(u => u.Id == peerId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var count = await db.DirectMessages
            .Where(m => m.SenderId == peerId && m.RecipientId == userId && m.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReadAt, now), cancellationToken)
            .ConfigureAwait(false);

        var read = new DirectMessagesRead(userId, peerId, now, count);
        if (count > 0)
        {
            var notification = new DirectMessagesReadNotification(read);
            hub.Publish(userId, notification);
            hub.Publish(peerId, notification);
        }

        return read;
    }

    private IQueryable<DirectMessage> Between(Guid userId, Guid peerId) =>
        db.DirectMessages.AsNoTracking().Where(m => (m.SenderId == userId && m.RecipientId == peerId) || (m.SenderId == peerId && m.RecipientId == userId));
}
