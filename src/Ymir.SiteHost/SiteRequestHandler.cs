using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Ymir.VibeMaker.Application.Files;
using Ymir.VibeMaker.Domain;
using Ymir.VibeMaker.Infrastructure.Persistence;
using Ymir.VibeMaker.Infrastructure.Sites;

namespace Ymir.SiteHost;

/// <summary>網站查詢結果（快取 10 秒）。</summary>
internal sealed record SiteInfo(Guid Id, Guid UserId, Guid VersionId, bool SpaMode, SiteAccessMode AccessMode);

/// <summary>
/// 提供網站檔案（ADR-0016 §5）：Host → 網站 → 權限 → 目前版本目錄內的檔案。
/// 未知、未發布、無權限一律回同樣的 404（不洩漏網站是否存在）；路徑逐段檢查並確認解析後仍在版本目錄內。
/// </summary>
internal sealed class SiteRequestHandler(
    SiteHostOptions options,
    VibeMakerDbContext db,
    IMemoryCache cache)
{
    private static readonly FileExtensionContentTypeProvider s_contentTypes = new();

    private readonly string _root = Path.GetFullPath(options.Root!);
    private readonly string _baseHost = options.BaseUrl!.Host.ToLowerInvariant();

    public async Task HandleAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        var site = SlugOf(context.Request.Host.Host) is { } slug ? await FindAsync(slug, context.RequestAborted).ConfigureAwait(false) : null;
        if (site is null || !await AuthorizeAsync(context, site).ConfigureAwait(false))
        {
            await NotFoundAsync(context).ConfigureAwait(false);
            return;
        }

        var file = Resolve(site, context.Request.Path.Value ?? "/");
        if (file is null)
        {
            await NotFoundAsync(context).ConfigureAwait(false);
            return;
        }

        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "same-origin";
        // 私人網站不得被共用快取保存（ADR-0016 §5）。
        headers.CacheControl = site.AccessMode == SiteAccessMode.Public ? "public, max-age=60" : "private, no-store";
        if (!s_contentTypes.TryGetContentType(file, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        await Results.File(file, contentType, enableRangeProcessing: true, lastModified: File.GetLastWriteTimeUtc(file)).ExecuteAsync(context).ConfigureAwait(false);
    }

    /// <summary>私人網站的權限檢查（H2）；首版只開放公開網站。</summary>
    private static Task<bool> AuthorizeAsync(HttpContext context, SiteInfo site) =>
        Task.FromResult(site.AccessMode == SiteAccessMode.Public);

    /// <summary><c>{slug}.{BaseHost}</c> 中的 slug；格式不符時回傳 null。</summary>
    internal string? SlugOf(string host)
    {
        var lower = host.ToLowerInvariant();
        if (!lower.EndsWith("." + _baseHost, StringComparison.Ordinal))
        {
            return null;
        }

        var slug = lower[..^(_baseHost.Length + 1)];
        return slug.Length == Site.SlugLength && slug.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9')) ? slug : null;
    }

    private async Task<SiteInfo?> FindAsync(string slug, CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync($"site:{slug}", async entry =>
        {
            // 取消發布、存取模式變更最慢 10 秒後生效。
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10);
            return await db.Sites
                .Where(s => s.Slug == slug && s.Status == SiteStatus.Published && s.CurrentVersionId != null)
                .Select(s => new SiteInfo(s.Id, s.UserId, s.CurrentVersionId!.Value, s.SpaMode, s.AccessMode))
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

    /// <summary>URL 路徑 → 版本目錄內的實體檔案；不安全或不存在時回傳 null。SPA 模式下沒有副檔名的路徑回 index.html。</summary>
    internal string? Resolve(SiteInfo site, string requestPath)
    {
        var directory = FileSystemSiteStorage.VersionDirectory(_root, site.Id, site.VersionId);
        var relative = requestPath.TrimStart('/');
        if (relative.Length == 0 || relative.EndsWith('/'))
        {
            relative += SiteIndex;
        }

        if (!WorkspacePathRules.IsSafeRelativePath(relative))
        {
            return null;
        }

        var file = Path.GetFullPath(Path.Combine(directory, relative));
        if (!file.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return null;
        }

        if (File.Exists(file))
        {
            return file;
        }

        if (Directory.Exists(file) && File.Exists(Path.Combine(file, SiteIndex)))
        {
            return Path.Combine(file, SiteIndex);
        }

        // SPA：沒有副檔名的路徑交給前端路由；資源檔不存在時仍是 404，不誤回 HTML（ADR-0016 §5）。
        return site.SpaMode && !Path.HasExtension(relative) && File.Exists(Path.Combine(directory, SiteIndex))
            ? Path.Combine(directory, SiteIndex)
            : null;
    }

    private const string SiteIndex = "index.html";

    private static async Task NotFoundAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsync("找不到這個網站或頁面。").ConfigureAwait(false);
    }
}
