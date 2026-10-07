using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Identity;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Contracts.Files;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Files;

/// <summary>
/// 對話的檔案（Agent 在工作目錄產生的成果）：列出、下載單檔、打包下載。
/// 只能存取目前使用者自己的對話（SA §12）；工作目錄由對話 / 專案的 Guid 推導（ADR-0007），不接受外部路徑。
/// </summary>
public sealed class WorkspaceFileService(IVibeMakerDbContext db, ICurrentUser currentUser, IWorkspaceFileReader reader)
{
    public const int MaxListedFiles = 1000;

    /// <summary>打包下載的上限，避免一次把整個 runtime 的大量檔案串出來。</summary>
    public const int MaxArchiveFiles = 500;

    public const long MaxArchiveBytes = 200L * 1024 * 1024;

    /// <summary>單檔下載上限。</summary>
    public const long MaxFileBytes = 200L * 1024 * 1024;

    /// <returns>對話不存在或不是自己的時回傳 null（→ 404）。</returns>
    public async Task<WorkspaceFilesResponse?> ListAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var target = await FindAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            return null;
        }

        // 還沒有執行過 Agent 的對話（或專案）不會有檔案，不必為了列檔案啟動 runtime。
        if (!target.Value.HasRun)
        {
            return new WorkspaceFilesResponse([], false);
        }

        var entries = await reader.ListAsync(currentUser.UserId, target.Value.WorkingDirectory, MaxListedFiles, cancellationToken).ConfigureAwait(false);
        return new WorkspaceFilesResponse(
            [.. entries.Take(MaxListedFiles).OrderBy(e => e.Path, StringComparer.Ordinal).Select(e => new WorkspaceFileResponse(e.Path, e.Size, e.ModifiedAt))],
            entries.Count > MaxListedFiles);
    }

    /// <summary>
    /// 下載單一檔案：檔案存在時呼叫 <paramref name="onFile"/>（在這裡才開始寫 HTTP 回應），否則回傳 NotFound。
    /// </summary>
    public async Task<WorkspaceFileOutcome> ReadFileAsync(
        Guid conversationId,
        string? path,
        Func<WorkspaceFileContent, CancellationToken, Task> onFile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onFile);
        if (!WorkspacePathRules.IsSafeRelativePath(path))
        {
            return WorkspaceFileOutcome.NotFound;
        }

        var target = await FindAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (target is not { HasRun: true } found)
        {
            return WorkspaceFileOutcome.NotFound;
        }

        var outcome = WorkspaceFileOutcome.NotFound;
        await reader.ReadAsync(currentUser.UserId, found.WorkingDirectory, [path!], async (file, ct) =>
        {
            if (file.Size > MaxFileBytes)
            {
                outcome = WorkspaceFileOutcome.TooLarge;
                return;
            }

            outcome = WorkspaceFileOutcome.Success;
            await onFile(file, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        return outcome;
    }

    /// <summary>打包下載前的檢查：回傳要打包的檔案清單與對話標題（當作 zip 檔名）。</summary>
    public async Task<WorkspaceArchivePlan> PlanArchiveAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var target = await FindAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            return WorkspaceArchivePlan.NotFound;
        }

        if (!target.Value.HasRun)
        {
            return new WorkspaceArchivePlan(WorkspaceFileOutcome.Success, [], target.Value.Title, target.Value.WorkingDirectory);
        }

        var entries = await reader.ListAsync(currentUser.UserId, target.Value.WorkingDirectory, MaxArchiveFiles, cancellationToken).ConfigureAwait(false);
        if (entries.Count > MaxArchiveFiles || entries.Sum(e => e.Size) > MaxArchiveBytes)
        {
            return new WorkspaceArchivePlan(WorkspaceFileOutcome.TooLarge, [], target.Value.Title, target.Value.WorkingDirectory);
        }

        return new WorkspaceArchivePlan(
            WorkspaceFileOutcome.Success,
            [.. entries.OrderBy(e => e.Path, StringComparer.Ordinal)],
            target.Value.Title,
            target.Value.WorkingDirectory);
    }

    /// <summary>依 <see cref="PlanArchiveAsync"/> 的清單讀取所有檔案（每個檔案呼叫一次 <paramref name="onFile"/>）。</summary>
    public Task ReadArchiveAsync(WorkspaceArchivePlan plan, Func<WorkspaceFileContent, CancellationToken, Task> onFile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Files.Count == 0
            ? Task.CompletedTask
            : reader.ReadAsync(currentUser.UserId, plan.WorkingDirectory, [.. plan.Files.Select(f => f.Path)], onFile, cancellationToken);
    }

    private async Task<(string WorkingDirectory, bool HasRun, string Title)?> FindAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var conversation = await db.Conversations.AsNoTracking()
            .Where(c => c.Id == conversationId && c.UserId == userId && c.Status == ConversationStatus.Active)
            .Select(c => new { c.Id, c.ProjectId, c.Title })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (conversation is null)
        {
            return null;
        }

        // 專案的對話共用同一個目錄：專案內任何一個對話執行過就可能有檔案。
        // 上傳過附件也會有檔案（附件放在工作目錄的 uploads/）。
        var hasRun = conversation.ProjectId is { } projectId
            ? await db.AgentExecutions.AnyAsync(e => e.UserId == userId && db.Conversations.Any(c => c.Id == e.ConversationId && c.ProjectId == projectId), cancellationToken).ConfigureAwait(false)
                || await db.MessageAttachments.AnyAsync(a => a.UserId == userId && db.Conversations.Any(c => c.Id == a.ConversationId && c.ProjectId == projectId), cancellationToken).ConfigureAwait(false)
            : await db.AgentExecutions.AnyAsync(e => e.ConversationId == conversation.Id, cancellationToken).ConfigureAwait(false)
                || await db.MessageAttachments.AnyAsync(a => a.ConversationId == conversation.Id, cancellationToken).ConfigureAwait(false);
        return (RuntimePaths.WorkingDirectoryFor(conversation.Id, conversation.ProjectId), hasRun, conversation.Title);
    }
}

public enum WorkspaceFileOutcome
{
    Success,
    NotFound,
    TooLarge,
}

public sealed record WorkspaceArchivePlan(WorkspaceFileOutcome Outcome, IReadOnlyList<WorkspaceFileEntry> Files, string Title, string WorkingDirectory)
{
    public static readonly WorkspaceArchivePlan NotFound = new(WorkspaceFileOutcome.NotFound, [], string.Empty, string.Empty);
}
