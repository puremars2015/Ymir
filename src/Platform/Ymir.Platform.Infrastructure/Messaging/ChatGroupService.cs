using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Auditing;
using Ymir.Platform.Infrastructure.Persistence;
using Ymir.Platform.Messaging;
using Ymir.Platform.Users;

namespace Ymir.Platform.Infrastructure.Messaging;

/// <summary>
/// 群組聊天（ADR-0026 §1）。每個查詢都以「目前使用者是未封存群組的成員」為條件，非成員一律回找不到。
/// 群組建立、改名、成員異動與封存寫稽核（只記 id）；訊息內容不寫稽核（ADR-0023）。
/// </summary>
internal sealed class ChatGroupService(PlatformDbContext db, IDirectMessageHub hub, IAuditLog auditLog, TimeProvider timeProvider) : IChatGroupService
{
    internal const int MaxPageSize = 100;

    public async Task<IReadOnlyList<ChatGroupSummary>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var groups = await (
                from member in db.ChatGroupMembers.AsNoTracking()
                join g in db.ChatGroups.AsNoTracking() on member.GroupId equals g.Id
                where member.UserId == userId && g.Status == ChatGroupStatus.Active
                select new { g.Id, g.Name, g.OwnerId, g.UpdatedAt, member.Role, member.LastReadAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (groups.Count == 0)
        {
            return [];
        }

        var ids = groups.Select(g => g.Id).ToList();
        var counts = await db.ChatGroupMembers.AsNoTracking().Where(m => ids.Contains(m.GroupId))
            .GroupBy(m => m.GroupId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken).ConfigureAwait(false);
        var last = await db.ChatGroupMessages.AsNoTracking().Where(m => ids.Contains(m.GroupId))
            .GroupBy(m => m.GroupId).Select(g => new { g.Key, Last = g.Max(m => m.CreatedAt) })
            .ToDictionaryAsync(g => g.Key, g => g.Last, cancellationToken).ConfigureAwait(false);

        var result = new List<ChatGroupSummary>(groups.Count);
        foreach (var g in groups)
        {
            var since = g.LastReadAt ?? DateTimeOffset.MinValue;
            var unread = await db.ChatGroupMessages.AsNoTracking()
                .CountAsync(m => m.GroupId == g.Id && m.CreatedAt > since && (m.SenderId == null || m.SenderId != userId), cancellationToken)
                .ConfigureAwait(false);
            result.Add(new ChatGroupSummary(g.Id, g.Name, g.OwnerId, g.Role, counts.GetValueOrDefault(g.Id), unread, last.TryGetValue(g.Id, out var at) ? at : null, g.UpdatedAt));
        }

        return [.. result.OrderByDescending(g => g.UnreadCount > 0).ThenByDescending(g => g.LastMessageAt ?? g.UpdatedAt)];
    }

    public async Task<ChatGroupResult> CreateAsync(Guid ownerId, string name, IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(memberIds);
        if (ChatGroup.NormalizeName(name) is null)
        {
            return Invalid($"群組名稱不可空白，且不能超過 {ChatGroup.NameMaxLength} 字。");
        }

        var others = memberIds.Where(id => id != ownerId).Distinct().ToList();
        if (others.Count + 1 > ChatGroup.MaxMembers)
        {
            return Invalid($"群組最多 {ChatGroup.MaxMembers} 人。");
        }

        if (!await AllActiveAsync(others, cancellationToken).ConfigureAwait(false))
        {
            return Invalid("有成員不存在或已停用。");
        }

        var now = timeProvider.GetUtcNow();
        var group = ChatGroup.Create(ownerId, name, now);
        db.ChatGroups.Add(group);
        db.ChatGroupMembers.Add(ChatGroupMember.Create(group.Id, ownerId, ChatGroupRole.Owner, now));
        foreach (var memberId in others)
        {
            db.ChatGroupMembers.Add(ChatGroupMember.Create(group.Id, memberId, ChatGroupRole.Member, now));
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await AuditAsync(ownerId, "group.create", group.Id, cancellationToken).ConfigureAwait(false);
        await NotifyUpdatedAsync(group.Id, [], cancellationToken).ConfigureAwait(false);
        return new ChatGroupResult(ChatGroupOutcome.Ok, await GetAsync(ownerId, group.Id, cancellationToken).ConfigureAwait(false));
    }

    public async Task<ChatGroupDetail?> GetAsync(Guid userId, Guid groupId, CancellationToken cancellationToken)
    {
        var group = await FindForMemberAsync(userId, groupId, tracking: false, cancellationToken).ConfigureAwait(false);
        if (group is null)
        {
            return null;
        }

        var members = await (
                from m in db.ChatGroupMembers.AsNoTracking()
                join u in db.Users.AsNoTracking() on m.UserId equals u.Id
                where m.GroupId == groupId
                select new ChatGroupMemberItem(u.Id, u.DisplayName, u.AccountName, u.Department, m.Role))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new ChatGroupDetail(
            group.Id,
            group.Name,
            group.OwnerId,
            group.CreatedAt,
            [.. members.OrderBy(m => m.Role).ThenBy(m => m.DisplayName, StringComparer.CurrentCulture)]);
    }

    public async Task<ChatGroupResult> RenameAsync(Guid userId, Guid groupId, string name, CancellationToken cancellationToken)
    {
        var group = await FindForMemberAsync(userId, groupId, tracking: true, cancellationToken).ConfigureAwait(false);
        if (group is null)
        {
            return new ChatGroupResult(ChatGroupOutcome.NotFound);
        }

        if (group.OwnerId != userId)
        {
            return new ChatGroupResult(ChatGroupOutcome.Forbidden);
        }

        if (ChatGroup.NormalizeName(name) is null)
        {
            return Invalid($"群組名稱不可空白，且不能超過 {ChatGroup.NameMaxLength} 字。");
        }

        group.Rename(name, timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await AuditAsync(userId, "group.rename", groupId, cancellationToken).ConfigureAwait(false);
        await NotifyUpdatedAsync(groupId, [], cancellationToken).ConfigureAwait(false);
        return new ChatGroupResult(ChatGroupOutcome.Ok, await GetAsync(userId, groupId, cancellationToken).ConfigureAwait(false));
    }

    public async Task<ChatGroupResult> AddMembersAsync(Guid userId, Guid groupId, IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(memberIds);
        var group = await FindForMemberAsync(userId, groupId, tracking: true, cancellationToken).ConfigureAwait(false);
        if (group is null)
        {
            return new ChatGroupResult(ChatGroupOutcome.NotFound);
        }

        if (group.OwnerId != userId)
        {
            return new ChatGroupResult(ChatGroupOutcome.Forbidden);
        }

        var existing = await db.ChatGroupMembers.Where(m => m.GroupId == groupId).Select(m => m.UserId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var added = memberIds.Distinct().Where(id => !existing.Contains(id)).ToList();
        if (existing.Count + added.Count > ChatGroup.MaxMembers)
        {
            return Invalid($"群組最多 {ChatGroup.MaxMembers} 人。");
        }

        if (!await AllActiveAsync(added, cancellationToken).ConfigureAwait(false))
        {
            return Invalid("有成員不存在或已停用。");
        }

        var now = timeProvider.GetUtcNow();
        foreach (var memberId in added)
        {
            db.ChatGroupMembers.Add(ChatGroupMember.Create(groupId, memberId, ChatGroupRole.Member, now));
        }

        group.Touch(now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (added.Count > 0)
        {
            await AuditAsync(userId, "group.members.add", groupId, cancellationToken).ConfigureAwait(false);
            await NotifyUpdatedAsync(groupId, [], cancellationToken).ConfigureAwait(false);
        }

        return new ChatGroupResult(ChatGroupOutcome.Ok, await GetAsync(userId, groupId, cancellationToken).ConfigureAwait(false));
    }

    public async Task<ChatGroupResult> RemoveMemberAsync(Guid userId, Guid groupId, Guid memberId, CancellationToken cancellationToken)
    {
        var group = await FindForMemberAsync(userId, groupId, tracking: true, cancellationToken).ConfigureAwait(false);
        if (group is null)
        {
            return new ChatGroupResult(ChatGroupOutcome.NotFound);
        }

        var leaving = memberId == userId;
        if (leaving && group.OwnerId == userId)
        {
            return Invalid("擁有者不能退出群組；請改為封存群組。");
        }

        if (!leaving && group.OwnerId != userId)
        {
            return new ChatGroupResult(ChatGroupOutcome.Forbidden);
        }

        var member = await db.ChatGroupMembers.SingleOrDefaultAsync(m => m.GroupId == groupId && m.UserId == memberId, cancellationToken).ConfigureAwait(false);
        if (member is null)
        {
            return new ChatGroupResult(ChatGroupOutcome.NotFound);
        }

        db.ChatGroupMembers.Remove(member);
        group.Touch(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await AuditAsync(userId, leaving ? "group.leave" : "group.members.remove", groupId, cancellationToken).ConfigureAwait(false);
        await NotifyUpdatedAsync(groupId, [memberId], cancellationToken).ConfigureAwait(false);
        return new ChatGroupResult(ChatGroupOutcome.Ok, leaving ? null : await GetAsync(userId, groupId, cancellationToken).ConfigureAwait(false));
    }

    public async Task<ChatGroupResult> ArchiveAsync(Guid userId, Guid groupId, CancellationToken cancellationToken)
    {
        var group = await FindForMemberAsync(userId, groupId, tracking: true, cancellationToken).ConfigureAwait(false);
        if (group is null)
        {
            return new ChatGroupResult(ChatGroupOutcome.NotFound);
        }

        if (group.OwnerId != userId)
        {
            return new ChatGroupResult(ChatGroupOutcome.Forbidden);
        }

        var members = await MemberIdsAsync(groupId, cancellationToken).ConfigureAwait(false);
        group.Archive(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await AuditAsync(userId, "group.archive", groupId, cancellationToken).ConfigureAwait(false);
        foreach (var memberId in members)
        {
            hub.Publish(memberId, new GroupUpdatedNotification(groupId, Removed: true));
        }

        return new ChatGroupResult(ChatGroupOutcome.Ok);
    }

    public async Task<GroupMessagePage?> GetMessagesAsync(Guid userId, Guid groupId, Guid? before, int take, CancellationToken cancellationToken)
    {
        if (await FindForMemberAsync(userId, groupId, tracking: false, cancellationToken).ConfigureAwait(false) is null)
        {
            return null;
        }

        var query = db.ChatGroupMessages.AsNoTracking().Where(m => m.GroupId == groupId);
        if (before is { } cursor)
        {
            var cursorAt = await db.ChatGroupMessages.AsNoTracking().Where(m => m.GroupId == groupId && m.Id == cursor)
                .Select(m => (DateTimeOffset?)m.CreatedAt).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (cursorAt is null)
            {
                return new GroupMessagePage([], false);
            }

            // 與 ADR-0023 相同：SQL Server 的 uniqueidentifier 排序不是時間順序，用 CreatedAt 當游標。
            query = query.Where(m => m.CreatedAt < cursorAt.Value);
        }

        var size = Math.Clamp(take, 1, MaxPageSize);
        var page = await query.OrderByDescending(m => m.CreatedAt).Take(size + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        var items = await ToItemsAsync([.. page.Take(size).Reverse()], cancellationToken).ConfigureAwait(false);
        return new GroupMessagePage(items, page.Count > size);
    }

    public async Task<GroupMessageSendResult> SendAsync(Guid userId, Guid groupId, string body, CancellationToken cancellationToken)
    {
        if (DirectMessage.NormalizeBody(body) is null)
        {
            return new(GroupMessageSendOutcome.InvalidBody, null);
        }

        var group = await FindForMemberAsync(userId, groupId, tracking: true, cancellationToken).ConfigureAwait(false);
        if (group is null)
        {
            return new(GroupMessageSendOutcome.NotFound, null);
        }

        var now = timeProvider.GetUtcNow();
        var message = ChatGroupMessage.CreateUser(groupId, userId, body, now);
        db.ChatGroupMessages.Add(message);
        group.Touch(now);
        // 自己送出的訊息之前都算讀過。
        (await db.ChatGroupMembers.SingleAsync(m => m.GroupId == groupId && m.UserId == userId, cancellationToken).ConfigureAwait(false)).MarkRead(now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var item = (await ToItemsAsync([message], cancellationToken).ConfigureAwait(false))[0];
        await PublishToMembersAsync(groupId, new GroupMessageNotification(item), cancellationToken).ConfigureAwait(false);
        return new(GroupMessageSendOutcome.Sent, item);
    }

    public async Task<GroupRead?> MarkReadAsync(Guid userId, Guid groupId, CancellationToken cancellationToken)
    {
        if (await FindForMemberAsync(userId, groupId, tracking: false, cancellationToken).ConfigureAwait(false) is null)
        {
            return null;
        }

        var member = await db.ChatGroupMembers.SingleAsync(m => m.GroupId == groupId && m.UserId == userId, cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        member.MarkRead(now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var read = new GroupRead(groupId, userId, now);
        hub.Publish(userId, new GroupReadNotification(read));
        return read;
    }

    public async Task<bool> IsMemberAsync(Guid userId, Guid groupId, CancellationToken cancellationToken) =>
        await FindForMemberAsync(userId, groupId, tracking: false, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<IReadOnlyList<GroupMessageItem>> GetRecentAsync(Guid groupId, int take, CancellationToken cancellationToken)
    {
        var page = await db.ChatGroupMessages.AsNoTracking()
            .Where(m => m.GroupId == groupId && (m.BotStatus == null || m.BotStatus != BotReplyStatus.Pending))
            .OrderByDescending(m => m.CreatedAt)
            .Take(Math.Clamp(take, 1, MaxPageSize))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return await ToItemsAsync([.. Enumerable.Reverse(page)], cancellationToken).ConfigureAwait(false);
    }

    public async Task<GroupMessageItem> AddBotPendingAsync(Guid groupId, Guid invokedById, Guid executionId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var message = ChatGroupMessage.CreateBotPending(groupId, invokedById, executionId, now);
        db.ChatGroupMessages.Add(message);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var item = (await ToItemsAsync([message], cancellationToken).ConfigureAwait(false))[0];
        await PublishToMembersAsync(groupId, new GroupMessageNotification(item), cancellationToken).ConfigureAwait(false);
        return item;
    }

    public async Task CompleteBotAsync(Guid messageId, string body, bool failed, CancellationToken cancellationToken)
    {
        var message = await db.ChatGroupMessages.SingleOrDefaultAsync(m => m.Id == messageId, cancellationToken).ConfigureAwait(false);
        if (message is null || message.BotStatus != BotReplyStatus.Pending)
        {
            return;
        }

        message.CompleteBot(body, failed);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var item = (await ToItemsAsync([message], cancellationToken).ConfigureAwait(false))[0];
        await PublishToMembersAsync(message.GroupId, new GroupMessageNotification(item), cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PendingBotReply>> ListPendingBotRepliesAsync(CancellationToken cancellationToken) =>
        await db.ChatGroupMessages.AsNoTracking()
            .Where(m => m.BotStatus == BotReplyStatus.Pending && m.ExecutionId != null)
            .OrderBy(m => m.CreatedAt)
            .Take(200)
            .Select(m => new PendingBotReply(m.Id, m.GroupId, m.ExecutionId!.Value, m.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<Guid, string>> FindNamesAsync(IReadOnlyCollection<Guid> groupIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groupIds);
        if (groupIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var ids = groupIds.ToList();
        return await db.ChatGroups.AsNoTracking().Where(g => ids.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> IsGroupExecutionAsync(Guid groupId, Guid executionId, CancellationToken cancellationToken) =>
        db.ChatGroupMessages.AsNoTracking().AnyAsync(m => m.GroupId == groupId && m.ExecutionId == executionId, cancellationToken);

    private Task<ChatGroup?> FindForMemberAsync(Guid userId, Guid groupId, bool tracking, CancellationToken cancellationToken)
    {
        var groups = tracking ? db.ChatGroups : db.ChatGroups.AsNoTracking();
        return groups.Where(g => g.Id == groupId && g.Status == ChatGroupStatus.Active && db.ChatGroupMembers.Any(m => m.GroupId == g.Id && m.UserId == userId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<bool> AllActiveAsync(List<Guid> userIds, CancellationToken cancellationToken) =>
        userIds.Count == 0
        || await db.Users.AsNoTracking().CountAsync(u => userIds.Contains(u.Id) && u.Status == UserStatus.Active, cancellationToken).ConfigureAwait(false) == userIds.Count;

    private Task<List<Guid>> MemberIdsAsync(Guid groupId, CancellationToken cancellationToken) =>
        db.ChatGroupMembers.AsNoTracking().Where(m => m.GroupId == groupId).Select(m => m.UserId).ToListAsync(cancellationToken);

    private async Task PublishToMembersAsync(Guid groupId, DirectMessageNotification notification, CancellationToken cancellationToken)
    {
        foreach (var memberId in await MemberIdsAsync(groupId, cancellationToken).ConfigureAwait(false))
        {
            hub.Publish(memberId, notification);
        }
    }

    /// <summary>通知目前的成員，以及這次被移除的人（他們的前端要把群組從清單拿掉）。</summary>
    private async Task NotifyUpdatedAsync(Guid groupId, IReadOnlyCollection<Guid> removed, CancellationToken cancellationToken)
    {
        foreach (var memberId in await MemberIdsAsync(groupId, cancellationToken).ConfigureAwait(false))
        {
            hub.Publish(memberId, new GroupUpdatedNotification(groupId, Removed: false));
        }

        foreach (var removedId in removed)
        {
            hub.Publish(removedId, new GroupUpdatedNotification(groupId, Removed: true));
        }
    }

    private async Task<List<GroupMessageItem>> ToItemsAsync(IReadOnlyList<ChatGroupMessage> messages, CancellationToken cancellationToken)
    {
        var userIds = messages.SelectMany(m => new[] { m.SenderId, m.InvokedById }).OfType<Guid>().Distinct().ToList();
        var names = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken).ConfigureAwait(false);
        return [.. messages.Select(m => new GroupMessageItem(
            m.Id,
            m.GroupId,
            m.SenderKind,
            m.SenderId,
            m.SenderId is { } sender ? names.GetValueOrDefault(sender) : null,
            m.Body,
            m.CreatedAt,
            m.ExecutionId,
            m.InvokedById,
            m.InvokedById is { } invoker ? names.GetValueOrDefault(invoker) : null,
            m.BotStatus))];
    }

    private Task AuditAsync(Guid actorId, string action, Guid groupId, CancellationToken cancellationToken) =>
        auditLog.WriteAsync(new AuditEntry(AuditActor.ForUser(actorId), action, "chat-group", groupId.ToString("D"), AuditResult.Success, timeProvider.GetUtcNow(), null), cancellationToken);

    private static ChatGroupResult Invalid(string problem) => new(ChatGroupOutcome.Invalid, null, problem);
}
