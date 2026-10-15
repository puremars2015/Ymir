namespace Ymir.VibeMaker.Application.Connectors.OneDrive;

/// <summary>給選擇器的最小中繼資料；不含 token、eTag、預授權 URL 或 host 路徑。</summary>
public sealed record OneDriveBrowserItem(string Id, string Name, bool IsFolder, long Size, DateTimeOffset LastModified);

public sealed record OneDriveBrowserPage(string FolderId, string? ParentItemId, IReadOnlyList<OneDriveBrowserItem> Items);

/// <summary>沿用 connector 的個人授權瀏覽整個自己的 drive，不要求先選同步根目錄（ADR-0022 §3）。</summary>
public sealed class OneDriveBrowser(OneDriveConnectionService connections, IOneDriveClient graph)
{
    public async Task<OneDriveBrowserPage> BrowseAsync(Guid userId, string? itemId, CancellationToken cancellationToken)
    {
        ValidateItemId(itemId);
        var (token, driveId) = await GetContextAsync(userId, cancellationToken).ConfigureAwait(false);
        var folder = itemId is null
            ? await graph.GetRootAsync(token, cancellationToken).ConfigureAwait(false)
            : await graph.GetItemAsync(token, itemId, cancellationToken).ConfigureAwait(false);
        // /me/drive/root 本身已由 Graph 綁定當前使用者，根目錄可能省略 parentReference。
        // 使用者傳入的 item id 則一律要求明確 drive id，不能用同一例外放寬。
        var ownFolder = folder is not null && (BelongsToDrive(folder, driveId)
            || (itemId is null && folder.DriveId is null && !folder.IsRemote));
        if (folder is null || !folder.IsFolder || !ownFolder)
        {
            throw new OneDriveItemInvalidException();
        }

        var children = await graph.ListChildrenAsync(token, folder.Id, cancellationToken).ConfigureAwait(false);
        return new OneDriveBrowserPage(folder.Id, folder.ParentItemId,
            children.Where(item => BelongsToDrive(item, driveId)).Select(ToBrowserItem).ToArray());
    }

    public async Task<IReadOnlyList<OneDriveBrowserItem>> SearchAsync(Guid userId, string? query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 200 || query.Any(char.IsControl))
        {
            throw new OneDriveItemInvalidException();
        }

        var (token, driveId) = await GetContextAsync(userId, cancellationToken).ConfigureAwait(false);
        return (await graph.SearchAsync(token, query.Trim(), cancellationToken).ConfigureAwait(false))
            .Where(item => BelongsToDrive(item, driveId)).Take(50).Select(ToBrowserItem).ToArray();
    }

    private async Task<(string Token, string DriveId)> GetContextAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!await connections.IsAllowedAsync(userId, cancellationToken).ConfigureAwait(false))
        {
            throw new OneDriveNotAllowedException();
        }

        var status = await connections.GetStatusAsync(userId, cancellationToken).ConfigureAwait(false);
        if (!status.Available || status.State != OneDriveLinkState.Connected)
        {
            throw new OneDriveAuthorizationException("請先連結 OneDrive，或重新授權。");
        }

        var token = await connections.GetAccessTokenAsync(userId, cancellationToken).ConfigureAwait(false);
        var driveId = await graph.GetDriveIdAsync(token, cancellationToken).ConfigureAwait(false);
        return (token, driveId);
    }

    public static bool BelongsToDrive(DriveItemInfo item, string driveId) =>
        !item.IsRemote && string.Equals(item.DriveId, driveId, StringComparison.Ordinal);

    public static void ValidateItemId(string? itemId)
    {
        if (itemId is not null && (string.IsNullOrWhiteSpace(itemId) || itemId.Length > 300 || itemId.Any(char.IsControl)))
        {
            throw new OneDriveItemInvalidException();
        }
    }

    private static OneDriveBrowserItem ToBrowserItem(DriveItemInfo item) => new(item.Id, item.Name, item.IsFolder, item.Size, item.LastModified);
}

public sealed class OneDriveNotAllowedException() : OneDriveException("管理員尚未開放 OneDrive 連結。");

public sealed class OneDriveItemInvalidException() : OneDriveException("無法存取這個 OneDrive 項目或查詢條件無效。");
