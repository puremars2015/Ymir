using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Auditing;
using Ymir.Platform.Identity;
using Ymir.VibeMaker.Application.Files;
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
        var workingDirectory = RuntimePaths.WorkingDirectoryFor(conversation.Id, conversation.ProjectId);
        if (!await writer.WriteAsync(userId, workingDirectory, path, content, size, cancellationToken).ConfigureAwait(false))
        {
            return AttachmentUploadResult.Invalid;
        }

        var attachment = MessageAttachment.Create(id, conversation.Id, userId, name, path, AttachmentRules.DetectContentType(header.AsSpan(0, headerLength), name), size, now);
        db.MessageAttachments.Add(attachment);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditLog.WriteAsync(
            new AuditEntry(currentUser.ActorName, "attachment.upload", "attachment", id.ToString("D"), AuditResult.Success, now, null),
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
