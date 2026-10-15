using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Auditing;
using Ymir.Platform.Identity;
using Ymir.VibeMaker.Application.Files;
using Ymir.VibeMaker.Application.Groups;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Contracts.Attachments;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Attachments;

/// <summary>
/// 使用者上傳附件（圖片、影片、文件…）到自己的對話：檔案寫進 runtime 的工作目錄 <c>uploads/</c>，送出訊息時再綁定（SA §12：只能用自己的對話）。
/// </summary>
public sealed class AttachmentService(
    IVibeMakerDbContext db,
    ICurrentUser currentUser,
    IWorkspaceFileWriter writer,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    private const int SniffBytes = 16;

    /// <param name="content">可 seek 的內容（API 先把 request body 寫到暫存檔，才知道大小與檔頭）。</param>
    public async Task<AttachmentUploadResult> UploadAsync(Guid conversationId, string? fileName, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var userId = currentUser.UserId;
        var conversation = await db.Conversations.AsNoTracking()
            .Where(c => c.Id == conversationId && c.UserId == userId && c.Status == ConversationStatus.Active)
            .Select(c => new { c.Id, c.ProjectId })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (conversation is null)
        {
            return AttachmentUploadResult.NotFound;
        }

        return await StoreAsync(userId, userId, conversation.Id, conversation.ProjectId, fileName, content, "attachment.upload", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 群組成員上傳檔案給群組的 Agent（ADR-0026）：檔案寫進群組 Agent 對話的工作目錄（runtime 擁有者 = 群組 id），
    /// 上傳者記為目前使用者，之後只有上傳者能在自己的 @bot 訊息綁定。呼叫端（API）必須先確認目前使用者是這個群組的成員。
    /// </summary>
    public async Task<AttachmentUploadResult> UploadForGroupAsync(Guid groupId, string groupName, string? fileName, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var userId = currentUser.UserId;
        if (groupId == Guid.Empty || groupId == userId)
        {
            return AttachmentUploadResult.NotFound;
        }

        if (!IsAcceptable(fileName, content))
        {
            return content.Length > AttachmentRules.MaxFileBytes ? AttachmentUploadResult.TooLarge : AttachmentUploadResult.Invalid;
        }

        var conversation = await GroupConversations.GetOrCreateAsync(db, groupId, groupName, timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        return await StoreAsync(groupId, userId, conversation.Id, null, fileName, content, "attachment.upload.group", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 群組訊息要附上的檔案名稱（依指定順序）：必須是目前使用者上傳到這個群組、尚未送出的。任何一個不可用或超過上限時回傳 null。
    /// 送出前先檢查，避免群組訊息已經寫上檔名、@bot 才發現附件不能用。
    /// </summary>
    public async Task<IReadOnlyList<string>?> FindPendingGroupFileNamesAsync(Guid groupId, IReadOnlyList<Guid> attachmentIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attachmentIds);
        var ids = attachmentIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        if (ids.Count > AttachmentRules.MaxAttachmentsPerMessage)
        {
            return null;
        }

        var userId = currentUser.UserId;
        var found = await db.MessageAttachments.AsNoTracking()
            .Where(a => ids.Contains(a.Id) && a.UserId == userId && a.MessageId == null)
            .Join(
                db.Conversations.Where(c => c.UserId == groupId && c.Status == ConversationStatus.Active),
                a => a.ConversationId,
                c => c.Id,
                (a, _) => new { a.Id, a.FileName })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return found.Count == ids.Count ? [.. found.OrderBy(a => ids.IndexOf(a.Id)).Select(a => a.FileName)] : null;
    }

    private static bool IsAcceptable(string? fileName, Stream content) =>
        content.Length is > 0 and <= AttachmentRules.MaxFileBytes && AttachmentRules.SanitizeFileName(fileName) is not null;

    /// <param name="runtimeOwnerId">工作目錄的擁有者：個人對話是使用者本人，群組是群組 id。</param>
    /// <param name="uploaderId">上傳者：送出時只能綁定自己上傳的附件。</param>
    private async Task<AttachmentUploadResult> StoreAsync(
        Guid runtimeOwnerId,
        Guid uploaderId,
        Guid conversationId,
        Guid? projectId,
        string? fileName,
        Stream content,
        string auditAction,
        CancellationToken cancellationToken)
    {
        var size = content.Length;
        if (size > AttachmentRules.MaxFileBytes)
        {
            return AttachmentUploadResult.TooLarge;
        }

        var name = AttachmentRules.SanitizeFileName(fileName);
        if (name is null || size == 0)
        {
            return AttachmentUploadResult.Invalid;
        }

        var header = new byte[SniffBytes];
        content.Position = 0;
        var headerLength = await content.ReadAtLeastAsync(header, SniffBytes, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
        content.Position = 0;

        var now = timeProvider.GetUtcNow();
        var id = Guid.CreateVersion7(now);
        var path = AttachmentRules.StoragePath(id, name);
        var workingDirectory = RuntimePaths.WorkingDirectoryFor(conversationId, projectId);
        if (!await writer.WriteAsync(runtimeOwnerId, workingDirectory, path, content, size, cancellationToken).ConfigureAwait(false))
        {
            return AttachmentUploadResult.Invalid;
        }

        var attachment = MessageAttachment.Create(id, conversationId, uploaderId, name, path, AttachmentRules.DetectContentType(header.AsSpan(0, headerLength), name), size, now);
        db.MessageAttachments.Add(attachment);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditLog.WriteAsync(
            new AuditEntry(currentUser.ActorName, auditAction, "attachment", id.ToString("D"), AuditResult.Success, now, null),
            cancellationToken).ConfigureAwait(false);
        return new AttachmentUploadResult(AttachmentUploadOutcome.Uploaded, ToResponse(attachment));
    }

    public static AttachmentResponse ToResponse(MessageAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        return new AttachmentResponse(attachment.Id, attachment.FileName, attachment.Path, attachment.ContentType, attachment.Size);
    }
}

public sealed record AttachmentUploadResult(AttachmentUploadOutcome Outcome, AttachmentResponse? Attachment)
{
    public static readonly AttachmentUploadResult NotFound = new(AttachmentUploadOutcome.NotFound, null);
    public static readonly AttachmentUploadResult TooLarge = new(AttachmentUploadOutcome.TooLarge, null);
    public static readonly AttachmentUploadResult Invalid = new(AttachmentUploadOutcome.Invalid, null);
}

public enum AttachmentUploadOutcome
{
    Uploaded,
    NotFound,
    TooLarge,
    Invalid,
}
