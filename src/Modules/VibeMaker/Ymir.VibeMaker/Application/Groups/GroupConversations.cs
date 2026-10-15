using Microsoft.EntityFrameworkCore;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Groups;

/// <summary>
/// 群組的 Agent 對話（ADR-0026 §2）：擁有者 = 群組 id，第一次 @bot 或第一次上傳附件時建立。
/// @bot 與群組附件共用，確保附件寫進的工作目錄就是之後 @bot 執行的工作目錄。
/// 呼叫端必須先確認目前使用者是這個群組的成員。
/// </summary>
internal static class GroupConversations
{
    public static async Task<Conversation> GetOrCreateAsync(IVibeMakerDbContext db, Guid groupId, string groupName, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // 同時兩個第一次呼叫可能各建一個對話：一律使用最早的那個，多出來的空對話不影響。
        var existing = await db.Conversations
            .Where(c => c.UserId == groupId && c.Status == ConversationStatus.Active)
            .OrderBy(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var title = string.IsNullOrWhiteSpace(groupName) ? "群組 Agent" : groupName.Trim();
        var conversation = Conversation.Create(groupId, null, title.Length > Conversation.TitleMaxLength ? title[..Conversation.TitleMaxLength] : title, now);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return conversation;
    }
}
