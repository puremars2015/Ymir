using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ymir.Platform.Identity;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Contracts.Files;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Files;

/// <summary>明確交付成果；所有外部存取驗證擁有者及成功執行紀錄。</summary>
public sealed class ArtifactService(IVibeMakerDbContext db, ICurrentUser user, IWorkspaceFileReader reader, ILogger<ArtifactService> logger)
{
    public static string DirectoryFor(Guid executionId) => $"deliverables/{executionId:N}";
    public static string PromptFor(Guid executionId) => $"""
        成果交付規則（本次執行）：唯一交付目錄是 {DirectoryFor(executionId)}/，相對於目前工作目錄。
        只有準備交給使用者下載的完整成果才放入該目錄。純閱讀、分析、摘要直接回覆，不建立成果檔，除非使用者要求可下載文件。
        單一成果直接放檔案，多檔成果保留目錄結構；平台自動提供 ZIP，不需要自行壓縮。
        網站或程式成果包含原始碼、必要設定（包含 package.json、lockfile）及使用說明，不得包含 node_modules、快取、憑證或工具暫存。
        工具安裝放 .ymir/tools/，暫存放 .ymir/tmp/；不要為了處理附件在工作目錄根建立 npm 專案。
        讀取 PDF 優先使用 pdftotext -layout <檔案> -。其他處理工具若需要安裝，也放在 .ymir/tools/。
        不得修改歷史 deliverables 目錄；更新成果時複製到本次交付目錄。不要把舊成果或上傳附件原封不動當成本次成果。
        只有本次指定交付目錄中、成功執行後經平台登記的檔案才有下載卡片，其他新增檔案不是交付成果。
        """;

    public async Task RegisterAsync(AgentExecution execution, string workingDirectory, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(execution);
        var files = await reader.ListDirectoryAsync(execution.UserId, workingDirectory, DirectoryFor(execution.Id), WorkspaceFileService.MaxArchiveFiles, ct).ConfigureAwait(false);
        if (files.Count > WorkspaceFileService.MaxArchiveFiles || files.Sum(f => f.Size) > WorkspaceFileService.MaxArchiveBytes
            || files.Any(f => !WorkspacePathRules.IsSafeRelativePath(f.Path) || f.Size > WorkspaceFileService.MaxFileBytes))
        {
            logger.LogWarning("Artifact delivery rejected for execution {ExecutionId}: limits exceeded", execution.Id);
            return;
        }
        if (files.Count == 0) logger.LogDebug("No deliverables registered for execution {ExecutionId}", execution.Id);
        db.ExecutionArtifacts.AddRange(files.Select(f => ExecutionArtifact.Create(execution.Id, f.Path, f.Size, f.ModifiedAt)));
    }

    public async Task<List<ArtifactGroupResponse>?> ListAsync(Guid conversationId, CancellationToken ct)
    {
        var conversation = await FindConversationAsync(conversationId, ct).ConfigureAwait(false);
        if (conversation is null) return null;
        var executions = await ExecutionsFor(conversation).Where(e => db.ExecutionArtifacts.Any(a => a.ExecutionId == e.Id))
            .OrderByDescending(e => e.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        var ids = executions.Select(e => e.Id).ToList();
        var artifacts = await db.ExecutionArtifacts.AsNoTracking().Where(a => ids.Contains(a.ExecutionId)).ToListAsync(ct).ConfigureAwait(false);
        return [.. executions.Select(e => new ArtifactGroupResponse(e.Id, e.ConversationId, e.AssistantMessageId, e.EndedAt ?? e.CreatedAt,
            [.. artifacts.Where(a => a.ExecutionId == e.Id).OrderBy(a => a.Path, StringComparer.Ordinal).Select(a => new WorkspaceFileResponse(a.Path, a.Size, a.ModifiedAt))]))];
    }

    public async Task<WorkspaceArchivePlan> PlanAsync(Guid conversationId, Guid executionId, CancellationToken ct)
    {
        var conversation = await FindConversationAsync(conversationId, ct).ConfigureAwait(false);
        if (conversation is null || !await ExecutionsFor(conversation).AnyAsync(e => e.Id == executionId, ct).ConfigureAwait(false)) return WorkspaceArchivePlan.NotFound;
        var files = await db.ExecutionArtifacts.AsNoTracking().Where(a => a.ExecutionId == executionId).OrderBy(a => a.Path).ToListAsync(ct).ConfigureAwait(false);
        if (files.Count == 0) return WorkspaceArchivePlan.NotFound;
        if (files.Count > WorkspaceFileService.MaxArchiveFiles || files.Sum(f => f.Size) > WorkspaceFileService.MaxArchiveBytes)
            return new WorkspaceArchivePlan(WorkspaceFileOutcome.TooLarge, [], conversation.Title, string.Empty);
        var directory = RuntimePaths.WorkingDirectoryFor(conversationId, conversation.ProjectId);
        var actual = await reader.ListDirectoryAsync(user.UserId, directory, DirectoryFor(executionId), WorkspaceFileService.MaxArchiveFiles, ct).ConfigureAwait(false);
        var byPath = actual.ToDictionary(f => f.Path, StringComparer.Ordinal);
        if (files.Any(f => !WorkspacePathRules.IsSafeRelativePath(f.Path) || !byPath.TryGetValue(f.Path, out var current) || current.Size != f.Size || current.ModifiedAt != f.ModifiedAt))
            return WorkspaceArchivePlan.NotFound;
        return new WorkspaceArchivePlan(WorkspaceFileOutcome.Success, [.. files.Select(f => new WorkspaceFileEntry(f.Path, f.Size, f.ModifiedAt))], conversation.Title, directory);
    }

    public async Task<WorkspaceFileOutcome> ReadFileAsync(Guid conversationId, Guid executionId, string? path, Func<WorkspaceFileContent, CancellationToken, Task> onFile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(onFile);
        if (!WorkspacePathRules.IsSafeRelativePath(path)) return WorkspaceFileOutcome.NotFound;
        var plan = await PlanAsync(conversationId, executionId, ct).ConfigureAwait(false);
        if (plan.Outcome != WorkspaceFileOutcome.Success) return plan.Outcome;
        var entry = plan.Files.SingleOrDefault(f => f.Path == path);
        if (entry is null) return WorkspaceFileOutcome.NotFound;
        await ReadArchiveAsync(plan with { Files = [entry] }, executionId, onFile, ct).ConfigureAwait(false);
        return WorkspaceFileOutcome.Success;
    }

    public async Task ReadArchiveAsync(WorkspaceArchivePlan plan, Guid executionId, Func<WorkspaceFileContent, CancellationToken, Task> onFile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(onFile);
        var prefix = DirectoryFor(executionId) + "/";
        var expected = plan.Files.ToDictionary(f => prefix + f.Path, StringComparer.Ordinal);
        var count = 0;
        await reader.ReadAsync(user.UserId, plan.WorkingDirectory, [.. expected.Keys], async (file, token) =>
        {
            if (!expected.TryGetValue(file.Path, out var entry) || file.Size != entry.Size || file.Size > WorkspaceFileService.MaxFileBytes)
                throw new IOException("Artifact changed during download.");
            await onFile(new WorkspaceFileContent(entry.Path, file.Size, file.Content), token).ConfigureAwait(false);
            count++;
        }, ct).ConfigureAwait(false);
        if (count != expected.Count) throw new IOException("Artifact missing during download.");
    }

    private IQueryable<AgentExecution> ExecutionsFor(Conversation conversation) => db.AgentExecutions.AsNoTracking()
        .Where(e => e.UserId == user.UserId && e.Status == ExecutionStatus.Completed
            && (conversation.ProjectId == null ? e.ConversationId == conversation.Id
                : db.Conversations.Any(c => c.Id == e.ConversationId && c.ProjectId == conversation.ProjectId && c.UserId == user.UserId && c.Status == ConversationStatus.Active)));

    private Task<Conversation?> FindConversationAsync(Guid id, CancellationToken ct) =>
        db.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id && c.UserId == user.UserId && c.Status == ConversationStatus.Active, ct);
}
