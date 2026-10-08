using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ymir.Platform.Auditing;
using Ymir.VibeMaker.Application.Files;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Sites;

public enum SitePublishError
{
    None,
    Disabled,
    NotFound,
    InvalidSource,
    MissingIndex,
    TooLarge,
    TooManySites,
    Failed,
}

/// <param name="Url">網站網址（<c>{slug}.{BaseDomain}</c>）。</param>
public sealed record SiteSummary(Site Site, Uri? Url, SiteVersion? CurrentVersion);

/// <summary>
/// 網站託管（ADR-0016）：把擁有者工作目錄中的一個目錄發布成網站版本、切換版本、取消發布與刪除。
/// 檔案經 <see cref="IWorkspaceFileReader"/> 在 runtime 內讀取（ADR-0008：API 不掛載 workspace），寫進網站 volume。
/// 所有操作只作用在呼叫端給的 user id 擁有的對話與網站（SA §12）。
/// </summary>
public sealed partial class SiteService(
    IVibeMakerDbContext db,
    IWorkspaceFileReader reader,
    ISiteStorage storage,
    SiteLocks locks,
    IOptions<SiteOptions> options,
    IAuditLog auditLog,
    TimeProvider timeProvider,
    ILogger<SiteService> logger)
{
    public const string IndexFile = "index.html";

    private readonly SiteOptions _options = options.Value;

    public bool IsEnabled => _options.IsEnabled;

    public SiteOptions Options => _options;

    public async Task<IReadOnlyList<SiteSummary>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var sites = await db.Sites.AsNoTracking().Where(s => s.UserId == userId).OrderByDescending(s => s.UpdatedAt).ToListAsync(cancellationToken).ConfigureAwait(false);
        var versionIds = sites.Where(s => s.CurrentVersionId is not null).Select(s => s.CurrentVersionId!.Value).ToList();
        var versions = await db.SiteVersions.AsNoTracking().Where(v => versionIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, cancellationToken).ConfigureAwait(false);
        return [.. sites.Select(s => Summary(s, s.CurrentVersionId is { } id ? versions.GetValueOrDefault(id) : null))];
    }

    public async Task<SiteSummary?> GetAsync(Guid userId, Guid siteId, CancellationToken cancellationToken)
    {
        var site = await db.Sites.AsNoTracking().SingleOrDefaultAsync(s => s.Id == siteId && s.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (site is null)
        {
            return null;
        }

        var version = site.CurrentVersionId is { } versionId
            ? await db.SiteVersions.AsNoTracking().SingleOrDefaultAsync(v => v.Id == versionId, cancellationToken).ConfigureAwait(false)
            : null;
        return Summary(site, version);
    }

    /// <summary>從對話的工作目錄建立並發布新網站。</summary>
    public async Task<(SiteSummary? Site, SitePublishError Error, string? Detail)> CreateAsync(
        Guid userId,
        Guid conversationId,
        string? name,
        string? sourcePath,
        bool spaMode,
        string actor,
        CancellationToken cancellationToken)
    {
        // 先確認擁有者：別人的對話一律 404，不透露其他狀態（SA §12）。
        if (await FindWorkingDirectoryAsync(userId, conversationId, cancellationToken).ConfigureAwait(false) is null)
        {
            return (null, SitePublishError.NotFound, null);
        }

        if (!IsEnabled)
        {
            return (null, SitePublishError.Disabled, null);
        }

        if (await db.Sites.CountAsync(s => s.UserId == userId, cancellationToken).ConfigureAwait(false) >= _options.MaxSitesPerUser)
        {
            return (null, SitePublishError.TooManySites, null);
        }

        var source = NormalizeSource(sourcePath);
        var title = string.IsNullOrWhiteSpace(name) ? "我的網站" : name.Trim();
        if (source is null || title.Length > Site.NameMaxLength)
        {
            return (null, SitePublishError.InvalidSource, null);
        }

        var now = timeProvider.GetUtcNow();
        var slug = await NewUniqueSlugAsync(cancellationToken).ConfigureAwait(false);
        var site = Site.Create(Guid.CreateVersion7(), userId, slug, title, conversationId, source, spaMode, now);
        db.Sites.Add(site);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var (error, detail) = await PublishCoreAsync(site, actor, cancellationToken).ConfigureAwait(false);
        if (error != SitePublishError.None)
        {
            // 第一次發布就失敗：不留下沒有內容的網站（不佔用配額）。
            db.Sites.Remove(site);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            storage.DeleteSite(site.Id);
            return (null, error, detail);
        }

        return (await GetAsync(userId, site.Id, cancellationToken).ConfigureAwait(false), error, detail);
    }

    /// <summary>重新發布（可改來源目錄、對話與 SPA 設定）；失敗時目前版本不變。</summary>
    public async Task<(SiteSummary? Site, SitePublishError Error, string? Detail)> RepublishAsync(
        Guid userId,
        Guid siteId,
        Guid? conversationId,
        string? sourcePath,
        bool? spaMode,
        string actor,
        CancellationToken cancellationToken)
    {
        var site = await db.Sites.SingleOrDefaultAsync(s => s.Id == siteId && s.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (site is null)
        {
            return (null, SitePublishError.NotFound, null);
        }

        if (!IsEnabled)
        {
            return (null, SitePublishError.Disabled, null);
        }

        var source = sourcePath is null ? site.SourcePath : NormalizeSource(sourcePath);
        var conversation = conversationId ?? site.ConversationId;
        if (source is null || await FindWorkingDirectoryAsync(userId, conversation, cancellationToken).ConfigureAwait(false) is null)
        {
            return (null, SitePublishError.InvalidSource, null);
        }

        site.Configure(site.Name, conversation, source, spaMode ?? site.SpaMode, timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var (error, detail) = await PublishCoreAsync(site, actor, cancellationToken).ConfigureAwait(false);
        return (await GetAsync(userId, site.Id, cancellationToken).ConfigureAwait(false), error, detail);
    }

    public async Task<SiteSummary?> UnpublishAsync(Guid userId, Guid siteId, string actor, CancellationToken cancellationToken)
    {
        var site = await db.Sites.SingleOrDefaultAsync(s => s.Id == siteId && s.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (site is null)
        {
            return null;
        }

        site.Unpublish(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await AuditAsync(actor, "site.unpublish", site.Id, cancellationToken).ConfigureAwait(false);
        return await GetAsync(userId, siteId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid siteId, string actor, CancellationToken cancellationToken)
    {
        var site = await db.Sites.SingleOrDefaultAsync(s => s.Id == siteId && s.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (site is null)
        {
            return false;
        }

        using var siteLock = await locks.AcquireAsync(siteId, cancellationToken).ConfigureAwait(false);
        db.Sites.Remove(site);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        storage.DeleteSite(siteId);
        await AuditAsync(actor, "site.delete", siteId, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>複製來源目錄成新版本，完整寫好後才切換（ADR-0016 §2）；失敗時刪除該版本目錄，目前版本不變。</summary>
    private async Task<(SitePublishError Error, string? Detail)> PublishCoreAsync(Site site, string actor, CancellationToken cancellationToken)
    {
        using var siteLock = await locks.AcquireAsync(site.Id, cancellationToken).ConfigureAwait(false);
        var workingDirectory = (await FindWorkingDirectoryAsync(site.UserId, site.ConversationId, cancellationToken).ConfigureAwait(false))!;
        var versionId = Guid.CreateVersion7();
        var now = timeProvider.GetUtcNow();
        var entries = site.SourcePath == "."
            ? await reader.ListAsync(site.UserId, workingDirectory, _options.MaxFiles, cancellationToken).ConfigureAwait(false)
            : await reader.ListDirectoryAsync(site.UserId, workingDirectory, site.SourcePath, _options.MaxFiles, cancellationToken).ConfigureAwait(false);

        (SitePublishError, string?) Fail(SitePublishError error, string detail)
        {
            db.SiteVersions.Add(SiteVersion.Failed(versionId, site.Id, site.SourcePath, detail, now));
            return (error, detail);
        }

        (SitePublishError Error, string? Detail) result;
        if (entries.Count > _options.MaxFiles || entries.Sum(e => e.Size) > _options.MaxBytes)
        {
            result = Fail(SitePublishError.TooLarge, $"網站最多 {_options.MaxFiles} 個檔案、{_options.MaxBytes / 1024 / 1024} MB。");
        }
        else if (!entries.Any(e => e.Path == IndexFile))
        {
            result = Fail(SitePublishError.MissingIndex, $"來源目錄「{site.SourcePath}」沒有 {IndexFile}。");
        }
        else
        {
            result = await CopyAsync(site, workingDirectory, versionId, entries, now, cancellationToken).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditLog.WriteAsync(
            new AuditEntry(actor, "site.publish", "site", site.Id.ToString("D"), result.Error == SitePublishError.None ? AuditResult.Success : AuditResult.Failure, now, null),
            cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task<(SitePublishError Error, string? Detail)> CopyAsync(
        Site site,
        string workingDirectory,
        Guid versionId,
        IReadOnlyList<WorkspaceFileEntry> entries,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var prefix = site.SourcePath == "." ? string.Empty : site.SourcePath + "/";
        var expected = entries.ToDictionary(e => prefix + e.Path, e => e.Path, StringComparer.Ordinal);
        var written = 0;
        long bytes = 0;
        try
        {
            await reader.ReadAsync(site.UserId, workingDirectory, [.. expected.Keys], async (file, ct) =>
            {
                if (!expected.TryGetValue(file.Path, out var relative) || (bytes += file.Size) > _options.MaxBytes)
                {
                    throw new IOException("Site source changed during publishing.");
                }

                await storage.WriteFileAsync(site.Id, versionId, relative, file.Content, ct).ConfigureAwait(false);
                written++;
            }, cancellationToken).ConfigureAwait(false);
            if (written != expected.Count)
            {
                throw new IOException("Site source changed during publishing.");
            }
        }
#pragma warning disable CA1031 // 發布失敗不得影響目前版本；原始例外只寫 server log，回應只有摘要。
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogPublishFailed(logger, ex, site.Id);
            storage.DeleteVersion(site.Id, versionId);
            db.SiteVersions.Add(SiteVersion.Failed(versionId, site.Id, site.SourcePath, "複製檔案失敗，請稍後重試。", now));
            return (SitePublishError.Failed, "複製檔案失敗，請稍後重試。");
        }

        db.SiteVersions.Add(SiteVersion.Ready(versionId, site.Id, site.SourcePath, written, bytes, now));
        site.Publish(versionId, now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await PruneAsync(site, cancellationToken).ConfigureAwait(false);
        return (SitePublishError.None, null);
    }

    /// <summary>只保留最近的幾個成功版本（目前版本一定保留）。</summary>
    private async Task PruneAsync(Site site, CancellationToken cancellationToken)
    {
        var old = await db.SiteVersions
            .Where(v => v.SiteId == site.Id && v.Status == SiteVersionStatus.Ready && v.Id != site.CurrentVersionId)
            .OrderByDescending(v => v.CreatedAt)
            .Skip(Math.Max(0, _options.KeepVersions - 1))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var version in old)
        {
            storage.DeleteVersion(site.Id, version.Id);
            version.Prune();
        }
    }

    private async Task<string?> FindWorkingDirectoryAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.AsNoTracking()
            .Where(c => c.Id == conversationId && c.UserId == userId && c.Status == ConversationStatus.Active)
            .Select(c => new { c.Id, c.ProjectId })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return conversation is null ? null : RuntimePaths.WorkingDirectoryFor(conversation.Id, conversation.ProjectId);
    }

    /// <summary>來源目錄：<c>.</c> 或通過 <see cref="WorkspacePathRules"/> 的相對路徑；不合法時回傳 null。</summary>
    internal static string? NormalizeSource(string? sourcePath)
    {
        var path = (sourcePath ?? ".").Trim().Trim('/');
        return path is "" or "." ? "." : WorkspacePathRules.IsSafeRelativePath(path) ? path : null;
    }

    private async Task<string> NewUniqueSlugAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var slug = SiteOptions.NewSlug();
            if (!await db.Sites.AnyAsync(s => s.Slug == slug, cancellationToken).ConfigureAwait(false))
            {
                return slug;
            }
        }
    }

    private SiteSummary Summary(Site site, SiteVersion? version) => new(site, _options.BaseUrl is null ? null : _options.UrlFor(site.Slug), version);

    private Task AuditAsync(string actor, string action, Guid siteId, CancellationToken cancellationToken) =>
        auditLog.WriteAsync(new AuditEntry(actor, action, "site", siteId.ToString("D"), AuditResult.Success, timeProvider.GetUtcNow(), null), cancellationToken);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publishing site {SiteId} failed")]
    private static partial void LogPublishFailed(ILogger logger, Exception exception, Guid siteId);
}
