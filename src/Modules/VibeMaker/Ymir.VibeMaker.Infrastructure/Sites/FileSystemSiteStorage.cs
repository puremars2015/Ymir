using Microsoft.Extensions.Options;
using Ymir.VibeMaker.Application.Files;
using Ymir.VibeMaker.Application.Sites;

namespace Ymir.VibeMaker.Infrastructure.Sites;

/// <summary>
/// 網站 volume（ADR-0016 §2）：<c>{Root}/{siteId:N}/{versionId:N}/{相對路徑}</c>。
/// 相對路徑已由 reader 的規則限制（不含 <c>..</c>、隱藏檔），這裡再確認解析後仍在版本目錄內。
/// </summary>
public sealed class FileSystemSiteStorage(IOptions<SiteOptions> options) : ISiteStorage
{
    /// <summary>用到時才解析：網站託管停用（沒有設定 Root）時不影響其他服務的建立。</summary>
    private string Root => Path.GetFullPath(options.Value.Root ?? throw new InvalidOperationException("Ymir:Sites:Root is not configured."));

    public static string VersionDirectory(string root, Guid siteId, Guid versionId) =>
        Path.Combine(root, siteId.ToString("N"), versionId.ToString("N"));

    public async Task WriteFileAsync(Guid siteId, Guid versionId, string relativePath, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!WorkspacePathRules.IsSafeRelativePath(relativePath))
        {
            throw new IOException("Unsafe site file path.");
        }

        var directory = VersionDirectory(Root, siteId, versionId);
        var target = Path.GetFullPath(Path.Combine(directory, relativePath));
        if (!target.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new IOException("Unsafe site file path.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await using (file.ConfigureAwait(false))
        {
            await content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }
    }

    public void DeleteVersion(Guid siteId, Guid versionId)
    {
        var directory = VersionDirectory(Root, siteId, versionId);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public void DeleteSite(Guid siteId)
    {
        var directory = Path.Combine(Root, siteId.ToString("N"));
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
