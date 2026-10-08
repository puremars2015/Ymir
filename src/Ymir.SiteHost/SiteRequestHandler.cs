using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Application.Files;
using Ymir.VibeMaker.Application.Sites;
using Ymir.VibeMaker.Domain;
using Ymir.VibeMaker.Infrastructure.Persistence;
using Ymir.VibeMaker.Infrastructure.Sites;

namespace Ymir.SiteHost;

/// <summary>網站查詢結果（快取 10 秒）。</summary>
internal sealed record SiteInfo(Guid Id, Guid UserId, Guid VersionId, bool SpaMode, SiteAccessMode AccessMode);

internal enum SiteAccess
{
    Allowed,

    /// <summary>私人網站、沒有有效的網站 cookie：導向 Ymir 登入取得票據。</summary>
    LoginRequired,

    Denied,
}

/// <summary>
/// 提供網站檔案（ADR-0016 §4、§5）：Host → 網站 → 權限 → 目前版本目錄內的檔案。
/// <list type="bullet">
/// <item>未知、未發布一律回同樣的 404（不洩漏網站是否存在）；路徑逐段檢查並確認解析後仍在版本目錄內。</item>
/// <item>私人網站以只限該 hostname 的 cookie 識別訪客；每個請求都重新檢查帳號狀態與授權（結果最多快取 30 秒）。</item>
/// </list>
/// </summary>
internal sealed class SiteRequestHandler(
    SiteHostOptions options,
    VibeMakerDbContext db,
    IUserDirectory users,
    IDataProtectionProvider dataProtection,
    IMemoryCache cache,
    TimeProvider timeProvider)
{
    public const string CookieName = "ymir_site";
    private const string SiteIndex = "index.html";
    private static readonly TimeSpan CookieLifetime = TimeSpan.FromHours(8);
    private static readonly FileExtensionContentTypeProvider s_contentTypes = new();

    private readonly string _root = Path.GetFullPath(options.Root!);
    private readonly string _baseHost = options.BaseUrl!.Host.ToLowerInvariant();
    private readonly IDataProtector _protector = dataProtection.CreateProtector("Ymir.Sites.Session.v1");

    public async Task HandleAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        var site = await SiteOfAsync(context).ConfigureAwait(false);
        if (site is null)
        {
            await TextAsync(context, StatusCodes.Status404NotFound, "找不到這個網站或頁面。").ConfigureAwait(false);
            return;
        }

        switch (await AuthorizeAsync(context, site).ConfigureAwait(false))
        {
            case SiteAccess.LoginRequired when options.PlatformUrl is { } platform:
                var path = context.Request.Path.Value + context.Request.QueryString.Value;
                context.Response.Redirect(new Uri(platform, $"site-access?site={site.Id:D}&path={Uri.EscapeDataString(SiteAccessRules.SafeReturnPath(path))}").ToString());
                return;
            case SiteAccess.LoginRequired or SiteAccess.Denied:
                await TextAsync(context, StatusCodes.Status403Forbidden, "沒有權限檢視這個網站，請洽網站擁有者。").ConfigureAwait(false);
                return;
        }

        var file = Resolve(site, context.Request.Path.Value ?? "/");
        if (file is null)
        {
            await TextAsync(context, StatusCodes.Status404NotFound, "找不到這個網站或頁面。").ConfigureAwait(false);
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

    /// <summary>
    /// 兌換 Ymir 簽發的一次性票據（ADR-0016 §4）：條件更新原子性地標記已使用（重放、過期、別的網站的票據都失敗），
    /// 成功後設定只限這個 hostname 的 cookie 並導回站內路徑。
    /// </summary>
    public async Task HandleTicketAsync(HttpContext context)
    {
        var site = await SiteOfAsync(context).ConfigureAwait(false);
        var ticket = context.Request.Query["ticket"].ToString();
        if (site is null || ticket.Length is 0 or > 200)
        {
            await TextAsync(context, StatusCodes.Status400BadRequest, "登入連結無效或已過期，請重新開啟網站。").ConfigureAwait(false);
            return;
        }

        var hash = SiteAccessRules.HashTicket(ticket);
        var now = timeProvider.GetUtcNow();
        var redeemed = await db.SiteTickets
            .Where(t => t.TokenHash == hash && t.SiteId == site.Id && t.UsedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(t => t.SetProperty(x => x.UsedAt, now), context.RequestAborted).ConfigureAwait(false);
        if (redeemed != 1)
        {
            await TextAsync(context, StatusCodes.Status400BadRequest, "登入連結無效或已過期，請重新開啟網站。").ConfigureAwait(false);
            return;
        }

        var userId = await db.SiteTickets.Where(t => t.TokenHash == hash).Select(t => t.UserId).SingleAsync(context.RequestAborted).ConfigureAwait(false);
        var expires = now + CookieLifetime;
        context.Response.Cookies.Append(CookieName, _protector.Protect($"{userId:N}|{site.Id:N}|{expires.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}"), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = options.BaseUrl!.Scheme == Uri.UriSchemeHttps,
            Path = "/",
            MaxAge = CookieLifetime,
        });
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Redirect(SiteAccessRules.SafeReturnPath(context.Request.Query["path"].ToString()));
    }

    /// <summary>私人網站：cookie 識別訪客，再檢查帳號狀態與授權（ADR-0016 §3）。</summary>
    private async Task<SiteAccess> AuthorizeAsync(HttpContext context, SiteInfo site)
    {
        if (site.AccessMode == SiteAccessMode.Public)
        {
            return SiteAccess.Allowed;
        }

        if (ViewerOf(context, site) is not { } viewer)
        {
            return SiteAccess.LoginRequired;
        }

        var allowed = await cache.GetOrCreateAsync($"access:{site.Id:N}:{viewer:N}", async entry =>
        {
            // 停用帳號、移除分享、改成私人等變更最慢 30 秒後生效。
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            var user = await users.FindAsync(viewer, context.RequestAborted).ConfigureAwait(false);
            if (user is null || user.Status != UserStatus.Active)
            {
                return false;
            }

            var shared = await db.SiteShares.AnyAsync(s => s.SiteId == site.Id && s.UserId == viewer, context.RequestAborted).ConfigureAwait(false);
            return SiteAccessRules.CanView(site.AccessMode, site.UserId, viewer, user.Role == UserRole.Admin, shared);
        }).ConfigureAwait(false);
        return allowed ? SiteAccess.Allowed : SiteAccess.Denied;
    }

    private Guid? ViewerOf(HttpContext context, SiteInfo site)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var value) || string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            var parts = _protector.Unprotect(value).Split('|');
            // cookie 綁定網站：同一個瀏覽器拿另一個網站的 cookie 來也無效。
            return parts.Length == 3
                && Guid.TryParseExact(parts[0], "N", out var userId)
                && parts[1] == site.Id.ToString("N")
                && long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var expires)
                && DateTimeOffset.FromUnixTimeSeconds(expires) > timeProvider.GetUtcNow()
                    ? userId
                    : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private Task<SiteInfo?> SiteOfAsync(HttpContext context) =>
        SlugOf(context.Request.Host.Host) is { } slug ? FindAsync(slug, context.RequestAborted) : Task.FromResult<SiteInfo?>(null);

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

    private static async Task TextAsync(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsync(message).ConfigureAwait(false);
    }
}
