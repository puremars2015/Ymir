using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ymir.VibeMaker.Application.Extensions;
using Ymir.VibeMaker.Application.Files;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Connectors.OneDrive;

/// <summary><c>Ymir:Connectors:OneDrive</c> 的同步限制（ADR-0013 §4）。</summary>
public sealed class OneDriveSyncOptions
{
    /// <summary>單檔大小上限；超過的檔案不同步（略過並提示）。</summary>
    public long MaxFileBytes { get; set; } = 100L * 1024 * 1024;

    /// <summary>一個工作目錄（或雲端資料夾）最多同步的檔案數；超過時停止同步並提示。</summary>
    public int MaxFiles { get; set; } = 5000;

    /// <summary>背景 worker 檢查待同步工作的間隔（排入工作時會立即喚醒，這只是保底）。</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>喚醒背景同步 worker：排入工作後立即處理，不必等下一次輪詢。</summary>
public sealed class OneDriveSyncSignal : IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // 已經有一個未處理的通知。
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _signal.WaitAsync(timeout, cancellationToken);

    public void Dispose() => _signal.Dispose();
}

public enum OneDriveAvailability
{
    /// <summary>管理員沒有開放 OneDrive 能力。</summary>
    NotAllowed,

    NotConnected,

    NeedsReauth,

    /// <summary>已連結但還沒選同步資料夾。</summary>
    NoRoot,

    Ready,
}

/// <summary>對話（工作目錄）的雲端保存狀態；只有摘要，不含 token 或 item id。</summary>
/// <param name="FolderPath">相對於使用者選定根資料夾的位置，例如 <c>chats/報告-1a2b3c4d</c>。</param>
public sealed record ConversationOneDriveStatus(
    OneDriveAvailability Availability,
    string? RootPath,
    string? FolderPath,
    OneDriveSyncState? State,
    DateTimeOffset? LastSyncedAt,
    int ConflictCount,
    string? LastError);

/// <summary>執行前下載的結果：<see cref="Active"/> 為 false 表示沒有啟用同步；<see cref="Warning"/> 是給使用者看的摘要。</summary>
public sealed record OneDriveDownloadResult(bool Active, int Conflicts, string? Warning)
{
    public static readonly OneDriveDownloadResult Inactive = new(false, 0, null);
}

/// <summary>
/// OneDrive 同步（ADR-0013 §4）：執行前下載雲端的變更，執行後（背景工作）上傳本機的新增與修改。
/// <list type="bullet">
/// <item>雲端版本以 eTag 比對、上傳帶 If-Match，不會無聲覆蓋；本機版本以大小 + 修改時間比對。</item>
/// <item>兩邊都改過時兩邊都保留：原檔名放雲端版本，本機版本另存「(OneDrive 衝突 時間)」副本，雲端與本機都有。</item>
/// <item>不同步刪除；隱藏檔、node_modules、超過大小上限或名稱不符合 OneDrive 規則的檔案略過；交付成果（deliverables）只上傳不下載。</item>
/// <item>呼叫端必須持有使用者的執行鎖（<see cref="Executions.UserExecutionLocks"/>），同步期間 Agent 不會同時寫檔。</item>
/// </list>
/// 檔案內容經 runtime 內的程序讀寫（<see cref="IWorkspaceFileReader"/> / <see cref="IWorkspaceFileWriter"/>），Agent container 拿不到 Graph token。
/// </summary>
public sealed partial class OneDriveSyncService(
    IVibeMakerDbContext db,
    IExtensionPolicy extensionPolicy,
    OneDriveConnectionService connections,
    IOneDriveClient graph,
    IWorkspaceFileReader reader,
    IWorkspaceFileWriter writer,
    OneDriveSyncSignal signal,
    IOptions<OneDriveSyncOptions> options,
    TimeProvider timeProvider,
    ILogger<OneDriveSyncService> logger)
{
    private const string DeliverablesPrefix = "deliverables/";
    private const string GenericFailure = "OneDrive 同步失敗，請稍後再試。";

    private readonly OneDriveSyncOptions _options = options.Value;

    /// <summary>對話的雲端保存狀態；對話不存在或不是自己的時回傳 null（→ 404）。</summary>
    public async Task<ConversationOneDriveStatus?> GetStatusAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var target = await FindTargetAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            return null;
        }

        var (availability, connection) = await GetAvailabilityAsync(userId, cancellationToken).ConfigureAwait(false);
        var scope = await db.OneDriveSyncScopes.AsNoTracking().SingleOrDefaultAsync(s => s.ScopeId == target.ScopeId && s.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        // 換了 drive 或根資料夾後，舊的對應已不適用，下一次同步會重新建立。
        var current = scope is not null && connection is not null && scope.DriveId == connection.DriveId && scope.RootItemId == connection.RootItemId;
        return new ConversationOneDriveStatus(
            availability,
            connection?.RootPath,
            current ? scope!.FolderPath : null,
            scope?.State,
            current ? scope!.LastSyncedAt : null,
            scope?.ConflictCount ?? 0,
            scope?.LastError);
    }

    /// <summary>手動同步 / 重試：排入背景工作。回傳 null 表示對話不存在；狀態不是 Ready 時不排入。</summary>
    public async Task<ConversationOneDriveStatus?> RequestSyncAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var target = await FindTargetAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            return null;
        }

        if ((await GetAvailabilityAsync(userId, cancellationToken).ConfigureAwait(false)).Availability == OneDriveAvailability.Ready)
        {
            var scope = await GetOrCreateScopeAsync(userId, target, cancellationToken).ConfigureAwait(false);
            var now = timeProvider.GetUtcNow();
            scope.StartRound(0, now);
            scope.Enqueue(conversationId, now);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            signal.Notify();
        }

        return await GetStatusAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>是否啟用同步：管理員開放、已連結、授權有效、已選同步資料夾。</summary>
    public async Task<bool> IsReadyAsync(Guid userId, CancellationToken cancellationToken) =>
        (await GetAvailabilityAsync(userId, cancellationToken).ConfigureAwait(false)).Availability == OneDriveAvailability.Ready;

    /// <summary>
    /// 執行前下載（<c>ExecutionRunner</c> 持有使用者鎖時呼叫）。同步失敗不阻擋執行：回傳警告，Agent 使用本機檔案。
    /// 取消（逾時、使用者停止）照常拋出。
    /// </summary>
    public async Task<OneDriveDownloadResult> DownloadBeforeRunAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var (availability, connection) = await GetAvailabilityAsync(userId, cancellationToken).ConfigureAwait(false);
        var target = availability == OneDriveAvailability.Ready ? await FindTargetAsync(userId, conversationId, cancellationToken).ConfigureAwait(false) : null;
        if (target is null)
        {
            return OneDriveDownloadResult.Inactive;
        }

        var scope = await GetOrCreateScopeAsync(userId, target, cancellationToken).ConfigureAwait(false);
        try
        {
            var run = await StartRunAsync(connection!, scope, target, cancellationToken).ConfigureAwait(false);
            await DownloadAsync(run, cancellationToken).ConfigureAwait(false);
            scope.StartRound(run.Conflicts, timeProvider.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new OneDriveDownloadResult(true, run.Conflicts, run.Failed > 0 ? $"有 {run.Failed} 個 OneDrive 檔案無法下載，這次使用本機版本。" : null);
        }
        catch (OneDriveException ex)
        {
            LogSyncFailed(logger, ex, target.ScopeId);
            await RecordDownloadFailureAsync(scope, ex.Message).ConfigureAwait(false);
            return new OneDriveDownloadResult(true, 0, ex.Message);
        }
#pragma warning disable CA1031 // 同步失敗不得讓 execution 失敗（ADR-0013 §4）；原始例外只寫 server log。
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogSyncFailed(logger, ex, target.ScopeId);
            await RecordDownloadFailureAsync(scope, GenericFailure).ConfigureAwait(false);
            return new OneDriveDownloadResult(true, 0, GenericFailure);
        }
    }

    /// <summary>執行結束後排入背景同步（上傳 Agent 新增或修改的檔案）；沒有啟用同步時不做事。</summary>
    public async Task EnqueueAfterRunAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        if ((await GetAvailabilityAsync(userId, cancellationToken).ConfigureAwait(false)).Availability != OneDriveAvailability.Ready
            || await FindTargetAsync(userId, conversationId, cancellationToken).ConfigureAwait(false) is not { } target)
        {
            return;
        }

        var scope = await GetOrCreateScopeAsync(userId, target, cancellationToken).ConfigureAwait(false);
        scope.Enqueue(conversationId, timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        signal.Notify();
    }

    /// <summary>到期的背景同步工作（worker 用）。</summary>
    public async Task<IReadOnlyList<(Guid ScopeId, Guid UserId)>> DueJobsAsync(int limit, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var jobs = await db.OneDriveSyncScopes.AsNoTracking()
            .Where(s => s.UploadPending && s.NextAttemptAt <= now)
            .OrderBy(s => s.NextAttemptAt)
            .Take(limit)
            .Select(s => new { s.ScopeId, s.UserId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. jobs.Select(j => (j.ScopeId, j.UserId))];
    }

    /// <summary>執行一個背景同步工作（下載 + 上傳）。呼叫端必須持有該使用者的執行鎖。</summary>
    public async Task RunJobAsync(Guid scopeId, CancellationToken cancellationToken)
    {
        var scope = await db.OneDriveSyncScopes.SingleOrDefaultAsync(s => s.ScopeId == scopeId, cancellationToken).ConfigureAwait(false);
        if (scope is not { UploadPending: true })
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var (availability, connection) = await GetAvailabilityAsync(scope.UserId, cancellationToken).ConfigureAwait(false);
        if (availability != OneDriveAvailability.Ready)
        {
            scope.AttemptFailed(availability == OneDriveAvailability.NeedsReauth ? "OneDrive 授權已失效，請重新連結後再同步。" : "OneDrive 同步已停用。", retry: false, now);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var target = await FindTargetAsync(scope.UserId, scope.ConversationId, cancellationToken).ConfigureAwait(false);
        if (target is null || target.ScopeId != scope.ScopeId)
        {
            scope.AttemptFailed("對話已封存，不再同步。", retry: false, now);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var run = await StartRunAsync(connection!, scope, target, cancellationToken).ConfigureAwait(false);
            await DownloadAsync(run, cancellationToken).ConfigureAwait(false);
            await UploadAsync(run, cancellationToken).ConfigureAwait(false);
            var (message, failed) = run.Failed > 0
                ? ($"有 {run.Failed} 個檔案無法同步，可以稍後重試。", true)
                : run.Skipped > 0
                    ? ($"略過 {run.Skipped} 個超過大小上限或名稱不符合 OneDrive 規則的檔案。", false)
                    : ((string?)null, false);
            scope.Succeeded(run.Conflicts, message, failed, timeProvider.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OneDriveAuthorizationException ex)
        {
            LogSyncFailed(logger, ex, scopeId);
            scope.AttemptFailed(ex.Message, retry: false, timeProvider.GetUtcNow());
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (OneDriveException ex)
        {
            LogSyncFailed(logger, ex, scopeId);
            scope.AttemptFailed(ex.Message, retry: true, timeProvider.GetUtcNow());
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // 單一同步工作失敗不得讓 worker 停止；排程重試，原始例外只寫 server log。
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogSyncFailed(logger, ex, scopeId);
            scope.AttemptFailed(GenericFailure, retry: true, timeProvider.GetUtcNow());
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task RecordDownloadFailureAsync(OneDriveSyncScope scope, string error)
    {
        scope.DownloadFailed(error, timeProvider.GetUtcNow());
        // 已經同步的檔案紀錄也一併保存，下次不會重複下載。
        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<(OneDriveAvailability Availability, OneDriveConnection? Connection)> GetAvailabilityAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!(await extensionPolicy.ResolveAsync(userId, cancellationToken).ConfigureAwait(false)).OneDrive)
        {
            return (OneDriveAvailability.NotAllowed, null);
        }

        var connection = await db.OneDriveConnections.AsNoTracking().SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken).ConfigureAwait(false);
        return connection switch
        {
            null => (OneDriveAvailability.NotConnected, null),
            { Status: OneDriveConnectionStatus.NeedsReauth } => (OneDriveAvailability.NeedsReauth, connection),
            { RootItemId: null } => (OneDriveAvailability.NoRoot, connection),
            _ => (OneDriveAvailability.Ready, connection),
        };
    }

    /// <summary>對話所屬的工作目錄：專案的對話共用專案目錄（ADR-0007）。只找目前使用者自己、未封存的對話（SA §12）。</summary>
    private async Task<SyncTarget?> FindTargetAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.AsNoTracking()
            .Where(c => c.Id == conversationId && c.UserId == userId && c.Status == ConversationStatus.Active)
            .Select(c => new { c.Id, c.ProjectId, c.Title })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (conversation is null)
        {
            return null;
        }

        if (conversation.ProjectId is not { } projectId)
        {
            return new SyncTarget(conversation.Id, RuntimePaths.WorkingDirectoryFor(conversation.Id, null), "chats", OneDrivePaths.FolderName(conversation.Title, conversation.Id));
        }

        var projectName = await db.Projects.AsNoTracking().Where(p => p.Id == projectId && p.UserId == userId).Select(p => p.Name).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return projectName is null
            ? null
            : new SyncTarget(projectId, RuntimePaths.WorkingDirectoryFor(conversation.Id, projectId), "projects", OneDrivePaths.FolderName(projectName, projectId));
    }

    private async Task<OneDriveSyncScope> GetOrCreateScopeAsync(Guid userId, SyncTarget target, CancellationToken cancellationToken)
    {
        var scope = await db.OneDriveSyncScopes.SingleOrDefaultAsync(s => s.ScopeId == target.ScopeId, cancellationToken).ConfigureAwait(false);
        if (scope is not null)
        {
            return scope.UserId == userId ? scope : throw new InvalidOperationException("OneDrive sync scope belongs to another user.");
        }

        scope = OneDriveSyncScope.Create(target.ScopeId, userId, target.ScopeId, timeProvider.GetUtcNow());
        db.OneDriveSyncScopes.Add(scope);
        return scope;
    }

    /// <summary>取得 access token、確認（或建立）雲端資料夾，並載入已同步的檔案紀錄。</summary>
    private async Task<SyncRun> StartRunAsync(OneDriveConnection connection, OneDriveSyncScope scope, SyncTarget target, CancellationToken cancellationToken)
    {
        var accessToken = await connections.GetAccessTokenAsync(scope.UserId, cancellationToken).ConfigureAwait(false);
        var items = await db.OneDriveSyncItems.Where(i => i.ScopeId == scope.ScopeId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var bound = scope.DriveId == connection.DriveId
            && scope.RootItemId == connection.RootItemId
            && scope.FolderItemId.Length > 0
            && await graph.GetItemAsync(accessToken, scope.FolderItemId, cancellationToken).ConfigureAwait(false) is { IsFolder: true };
        if (!bound)
        {
            // 第一次同步，或使用者換了 drive / 根資料夾、雲端資料夾被刪除：重新建立對應，舊的同步紀錄不再適用。
            var parent = await graph.EnsureFolderAsync(accessToken, connection.RootItemId!, target.ParentFolder, cancellationToken).ConfigureAwait(false);
            var folder = await graph.EnsureFolderAsync(accessToken, parent.Id, target.FolderName, cancellationToken).ConfigureAwait(false);
            scope.BindFolder(connection.DriveId, connection.RootItemId!, folder.Id, $"{target.ParentFolder}/{target.FolderName}", timeProvider.GetUtcNow());
            db.OneDriveSyncItems.RemoveRange(items);
            items.Clear();
        }

        var run = new SyncRun(scope.UserId, accessToken, scope, target.WorkingDirectory);
        foreach (var item in items)
        {
            if (!run.Items.TryAdd(item.Path, item))
            {
                db.OneDriveSyncItems.Remove(item);
            }
        }

        run.Folders[string.Empty] = scope.FolderItemId;
        return run;
    }

    private async Task DownloadAsync(SyncRun run, CancellationToken cancellationToken)
    {
        var cloud = await ListCloudAsync(run, cancellationToken).ConfigureAwait(false);
        var local = await ListLocalAsync(run, cancellationToken).ConfigureAwait(false);
        var written = new List<string>();
        foreach (var (path, item) in cloud.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            // 交付成果只由 Agent 產生（ArtifactService 以大小與修改時間驗證），不從雲端覆寫。
            if (path.StartsWith(DeliverablesPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (item.Size > _options.MaxFileBytes)
            {
                run.Skipped++;
                continue;
            }

            run.Items.TryGetValue(path, out var record);
            if (record is not null && record.ETag == item.ETag)
            {
                continue; // 雲端沒有變更
            }

            if (local.TryGetValue(path, out var localFile) && !(record is not null && record.MatchesLocal(localFile.Size, localFile.ModifiedAt)))
            {
                // 雲端與本機都改過（或兩邊各自新增同名檔案）：本機版本另存衝突副本，原檔名改為雲端版本。
                var conflictPath = OneDrivePaths.ConflictPath(path, timeProvider.GetUtcNow());
                if (!await CopyLocalAsync(run, path, conflictPath, cancellationToken).ConfigureAwait(false))
                {
                    run.Failed++;
                    continue;
                }

                run.Conflicts++;
            }

            if (await WriteFromCloudAsync(run, path, item, cancellationToken).ConfigureAwait(false))
            {
                written.Add(path);
            }
            else
            {
                run.Failed++;
            }
        }

        await RefreshLocalAsync(run, written, cancellationToken).ConfigureAwait(false);
    }

    private async Task UploadAsync(SyncRun run, CancellationToken cancellationToken)
    {
        var local = await ListLocalAsync(run, cancellationToken).ConfigureAwait(false);
        var written = new List<string>();
        foreach (var (path, file) in local.OrderBy(l => l.Key, StringComparer.Ordinal))
        {
            if (file.Size > _options.MaxFileBytes || !OneDrivePaths.IsSyncablePath(path))
            {
                run.Skipped++;
                continue;
            }

            run.Items.TryGetValue(path, out var record);
            if (record is not null && record.MatchesLocal(file.Size, file.ModifiedAt))
            {
                continue; // 本機沒有變更
            }

            try
            {
                await UploadFileAsync(run, path, file, record, written, cancellationToken).ConfigureAwait(false);
            }
            catch (OneDriveException ex) when (ex is not OneDriveAuthorizationException)
            {
                // 單一檔案失敗（例如雲端同名的是檔案而不是資料夾）不影響其他檔案。
                LogFileFailed(logger, ex, run.Scope.ScopeId);
                run.Failed++;
            }
        }

        await RefreshLocalAsync(run, written, cancellationToken).ConfigureAwait(false);
    }

    private async Task UploadFileAsync(SyncRun run, string path, WorkspaceFileEntry file, OneDriveSyncItem? record, List<string> written, CancellationToken cancellationToken)
    {
        var slash = path.LastIndexOf('/');
        var directory = slash < 0 ? string.Empty : path[..slash];
        var name = path[(slash + 1)..];
        var parentId = await EnsureFolderPathAsync(run, directory, cancellationToken).ConfigureAwait(false);
        var result = await UploadLocalAsync(run, path, parentId, name, record?.ETag, record is null ? OneDriveConflictBehavior.Fail : OneDriveConflictBehavior.Replace, cancellationToken)
            .ConfigureAwait(false);
        if (result?.Item is { } uploaded)
        {
            Record(run, path).Update(uploaded.Id, uploaded.ETag, file.Size, file.ModifiedAt, timeProvider.GetUtcNow());
            return;
        }

        if (result is null)
        {
            run.Failed++; // 讀取時檔案已不存在
            return;
        }

        // 衝突：上次同步後雲端也改過，或雲端已有同名檔案。
        var current = await graph.GetChildAsync(run.AccessToken, parentId, name, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            // 雲端檔案已被刪除（不同步刪除）：本機有修改，重新建立。
            var recreated = await UploadLocalAsync(run, path, parentId, name, null, OneDriveConflictBehavior.Fail, cancellationToken).ConfigureAwait(false);
            if (recreated?.Item is { } item)
            {
                Record(run, path).Update(item.Id, item.ETag, file.Size, file.ModifiedAt, timeProvider.GetUtcNow());
            }
            else
            {
                run.Failed++;
            }

            return;
        }

        if (current.IsFolder || current.Size > _options.MaxFileBytes)
        {
            // 雲端同名的是資料夾（或過大）：無法以雲端版本取代本機檔案，略過，不產生衝突副本。
            run.Skipped++;
            return;
        }

        // 本機版本另存衝突副本（雲端與本機都有），原檔名改為雲端版本。
        var copy = await UploadLocalAsync(run, path, parentId, WorkspacePathRules.FileName(OneDrivePaths.ConflictPath(path, timeProvider.GetUtcNow())), null, OneDriveConflictBehavior.Rename, cancellationToken)
            .ConfigureAwait(false);
        var conflictPath = copy?.Item is { } copied ? (directory.Length == 0 ? copied.Name : $"{directory}/{copied.Name}") : null;
        if (conflictPath is null || !WorkspacePathRules.IsSafeRelativePath(conflictPath) || !await CopyLocalAsync(run, path, conflictPath, cancellationToken).ConfigureAwait(false))
        {
            run.Failed++;
            return;
        }

        Record(run, conflictPath).Update(copy!.Item!.Id, copy.Item.ETag, null, null, timeProvider.GetUtcNow());
        written.Add(conflictPath);
        if (await WriteFromCloudAsync(run, path, current, cancellationToken).ConfigureAwait(false))
        {
            written.Add(path);
        }
        else
        {
            run.Failed++;
        }

        run.Conflicts++;
    }

    /// <summary>遞迴列出雲端資料夾的檔案（相對路徑 → item），同時記下資料夾的 item id 供上傳使用。</summary>
    private async Task<Dictionary<string, DriveItemInfo>> ListCloudAsync(SyncRun run, CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, DriveItemInfo>(StringComparer.Ordinal);
        var pending = new Queue<(string Id, string Prefix)>();
        pending.Enqueue((run.Scope.FolderItemId, string.Empty));
        var count = 0;
        while (pending.TryDequeue(out var folder))
        {
            foreach (var child in await graph.ListChildrenAsync(run.AccessToken, folder.Id, cancellationToken).ConfigureAwait(false))
            {
                if (WorkspacePathRules.IsHidden(child.Name))
                {
                    continue;
                }

                if (++count > _options.MaxFiles)
                {
                    throw new OneDriveException($"OneDrive 資料夾的項目超過 {_options.MaxFiles} 個，已停止同步。");
                }

                var path = folder.Prefix + child.Name;
                if (child.IsFolder)
                {
                    run.Folders[path] = child.Id;
                    pending.Enqueue((child.Id, path + "/"));
                }
                else if (WorkspacePathRules.IsSafeRelativePath(path))
                {
                    files[path] = child;
                }
            }
        }

        return files;
    }

    /// <summary>本機工作目錄的檔案（含交付成果目錄），不含隱藏檔、node_modules、symlink。</summary>
    private async Task<Dictionary<string, WorkspaceFileEntry>> ListLocalAsync(SyncRun run, CancellationToken cancellationToken)
    {
        var entries = await reader.ListAsync(run.UserId, run.WorkingDirectory, _options.MaxFiles, cancellationToken).ConfigureAwait(false);
        var deliverables = await reader.ListDirectoryAsync(run.UserId, run.WorkingDirectory, DeliverablesPrefix.TrimEnd('/'), _options.MaxFiles, cancellationToken)
            .ConfigureAwait(false);
        if (entries.Count + deliverables.Count > _options.MaxFiles)
        {
            throw new OneDriveException($"工作目錄的檔案超過 {_options.MaxFiles} 個，已停止同步。");
        }

        var files = new Dictionary<string, WorkspaceFileEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            files[entry.Path] = entry;
        }

        foreach (var entry in deliverables)
        {
            files[DeliverablesPrefix + entry.Path] = entry with { Path = DeliverablesPrefix + entry.Path };
        }

        return files;
    }

    /// <summary>寫入後重新讀取本機的大小與修改時間，下次比對時才知道本機沒有再改過。</summary>
    private async Task RefreshLocalAsync(SyncRun run, List<string> written, CancellationToken cancellationToken)
    {
        if (written.Count == 0)
        {
            return;
        }

        var local = await ListLocalAsync(run, cancellationToken).ConfigureAwait(false);
        foreach (var path in written)
        {
            if (local.TryGetValue(path, out var file) && run.Items.TryGetValue(path, out var record))
            {
                record.SetLocal(file.Size, file.ModifiedAt);
            }
        }
    }

    private async Task<string> EnsureFolderPathAsync(SyncRun run, string directory, CancellationToken cancellationToken)
    {
        if (run.Folders.TryGetValue(directory, out var known))
        {
            return known;
        }

        var current = string.Empty;
        var parentId = run.Folders[string.Empty];
        foreach (var segment in directory.Split('/'))
        {
            current = current.Length == 0 ? segment : $"{current}/{segment}";
            if (!run.Folders.TryGetValue(current, out var id))
            {
                id = (await graph.EnsureFolderAsync(run.AccessToken, parentId, segment, cancellationToken).ConfigureAwait(false)).Id;
                run.Folders[current] = id;
            }

            parentId = id;
        }

        return parentId;
    }

    private async Task<bool> WriteFromCloudAsync(SyncRun run, string path, DriveItemInfo item, CancellationToken cancellationToken)
    {
        var content = await graph.OpenDownloadAsync(run.AccessToken, item.Id, cancellationToken).ConfigureAwait(false);
        await using (content.ConfigureAwait(false))
        {
            if (!await writer.WriteAsync(run.UserId, run.WorkingDirectory, path, content, item.Size, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }

        Record(run, path).Update(item.Id, item.ETag, null, null, timeProvider.GetUtcNow());
        return true;
    }

    /// <summary>在 runtime 內複製檔案（衝突副本）：經 reader 讀出再經 writer 寫入，路徑檢查同兩者。</summary>
    private async Task<bool> CopyLocalAsync(SyncRun run, string from, string to, CancellationToken cancellationToken)
    {
        var copied = false;
        await reader.ReadAsync(run.UserId, run.WorkingDirectory, [from], async (file, ct) =>
        {
            copied = await writer.WriteAsync(run.UserId, run.WorkingDirectory, to, file.Content, file.Size, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        return copied;
    }

    /// <returns>檔案已不存在時回傳 null。</returns>
    private async Task<OneDriveUploadResult?> UploadLocalAsync(
        SyncRun run,
        string path,
        string parentId,
        string name,
        string? ifMatch,
        OneDriveConflictBehavior behavior,
        CancellationToken cancellationToken)
    {
        OneDriveUploadResult? result = null;
        await reader.ReadAsync(run.UserId, run.WorkingDirectory, [path], async (file, ct) =>
        {
            result = await graph.UploadAsync(run.AccessToken, parentId, name, file.Content, file.Size, ifMatch, behavior, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private OneDriveSyncItem Record(SyncRun run, string path)
    {
        if (!run.Items.TryGetValue(path, out var record))
        {
            record = OneDriveSyncItem.Create(run.Scope.ScopeId, path, timeProvider.GetUtcNow());
            db.OneDriveSyncItems.Add(record);
            run.Items[path] = record;
        }

        return record;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "OneDrive sync failed for scope {ScopeId}")]
    private static partial void LogSyncFailed(ILogger logger, Exception exception, Guid scopeId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Uploading a file to OneDrive failed for scope {ScopeId}")]
    private static partial void LogFileFailed(ILogger logger, Exception exception, Guid scopeId);

    private sealed record SyncTarget(Guid ScopeId, string WorkingDirectory, string ParentFolder, string FolderName);

    private sealed class SyncRun(Guid userId, string accessToken, OneDriveSyncScope scope, string workingDirectory)
    {
        public Guid UserId { get; } = userId;

        public string AccessToken { get; } = accessToken;

        public OneDriveSyncScope Scope { get; } = scope;

        public string WorkingDirectory { get; } = workingDirectory;

        public Dictionary<string, OneDriveSyncItem> Items { get; } = new(StringComparer.Ordinal);

        /// <summary>雲端資料夾的相對路徑 → item id；空字串是這個工作目錄對應的資料夾。</summary>
        public Dictionary<string, string> Folders { get; } = new(StringComparer.Ordinal);

        public int Conflicts { get; set; }

        public int Failed { get; set; }

        public int Skipped { get; set; }
    }
}
