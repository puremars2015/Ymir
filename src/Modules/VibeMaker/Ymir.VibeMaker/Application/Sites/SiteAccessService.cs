using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ymir.Platform.Auditing;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Sites;

/// <summary>誰可以看網站（ADR-0016 §3）；API 簽發票據與 SiteHost 每個請求都用同一套規則。</summary>
public static class SiteAccessRules
{
    public static bool CanView(SiteAccessMode mode, Guid ownerId, Guid viewerId, bool viewerIsAdmin, bool isShared) =>
        mode == SiteAccessMode.Public
        || viewerId == ownerId
        || viewerIsAdmin
        || mode == SiteAccessMode.AllUsers
        || (mode == SiteAccessMode.SelectedUsers && isShared);

    public static string HashTicket(string ticket) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ticket)));

    /// <summary>站內相對路徑（以單一 <c>/</c> 開頭、不含反斜線與控制字元）；其他一律改成 <c>/</c>，避免開放式導向。</summary>
    public static string SafeReturnPath(string? path) =>
        path is { Length: > 0 and <= 2000 } p && p[0] == '/' && !p.StartsWith("//", StringComparison.Ordinal) && !p.Contains('\\', StringComparison.Ordinal) && !p.Any(char.IsControl)
            ? p
            : "/";
}

public sealed record SiteShareInfo(Guid UserId, string DisplayName, string? AccountName);

public sealed record SharedSite(Guid Id, string Name, Uri? Url, string OwnerName, DateTimeOffset UpdatedAt);

public enum SiteTicketOutcome
{
    Issued,
    NotFound,
    Forbidden,
}

/// <summary>
/// 網站的存取設定、分享名單與私人網站的登入票據（ADR-0016 §3、§4）。
/// 分享名單只接受存在且未停用的 Ymir 使用者（以 user id 保存）；票據只存雜湊、60 秒、只能用一次。
/// </summary>
public sealed class SiteAccessService(
    IVibeMakerDbContext db,
    IUserDirectory users,
    IOptions<SiteOptions> options,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public const int MaxShares = 200;

    /// <returns>找不到網站（或不是自己的）時回傳 null；分享名單不合法時回傳錯誤訊息。</returns>
    public async Task<(bool Found, string? Error)> SetAccessAsync(
        Guid userId,
        Guid siteId,
        SiteAccessMode mode,
        IReadOnlyCollection<Guid> sharedWith,
        string actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sharedWith);
        var site = await db.Sites.SingleOrDefaultAsync(s => s.Id == siteId && s.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (site is null)
        {
            return (false, null);
        }

        var ids = mode == SiteAccessMode.SelectedUsers ? sharedWith.Where(id => id != userId).Distinct().ToList() : [];
        if (ids.Count > MaxShares)
        {
            return (true, $"最多分享給 {MaxShares} 位使用者。");
        }

        if (ids.Count > 0 && (await users.FindManyAsync(ids, cancellationToken).ConfigureAwait(false)).Count(u => u.Status == UserStatus.Active) != ids.Count)
        {
            return (true, "分享名單中有不存在或已停用的使用者。");
        }

        var now = timeProvider.GetUtcNow();
        site.SetAccessMode(mode, now);
        var existing = await db.SiteShares.Where(s => s.SiteId == siteId).ToListAsync(cancellationToken).ConfigureAwait(false);
        db.SiteShares.RemoveRange(existing.Where(s => !ids.Contains(s.UserId)));
        db.SiteShares.AddRange(ids.Where(id => existing.All(s => s.UserId != id)).Select(id => SiteShare.Create(siteId, id, now)));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditLog.WriteAsync(new AuditEntry(actor, "site.access.update", "site", siteId.ToString("D"), AuditResult.Success, now, null), cancellationToken).ConfigureAwait(false);
        return (true, null);
    }

    /// <summary>多個網站的分享名單（擁有者的「我的網站」頁顯示用）。</summary>
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<SiteShareInfo>>> SharesAsync(IReadOnlyCollection<Guid> siteIds, CancellationToken cancellationToken)
    {
        var shares = await db.SiteShares.AsNoTracking().Where(s => siteIds.Contains(s.SiteId)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var names = (await users.FindManyAsync([.. shares.Select(s => s.UserId).Distinct()], cancellationToken).ConfigureAwait(false)).ToDictionary(u => u.Id);
        return shares.GroupBy(s => s.SiteId).ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<SiteShareInfo>)[.. g.Where(s => names.ContainsKey(s.UserId)).Select(s => new SiteShareInfo(s.UserId, names[s.UserId].DisplayName, names[s.UserId].AccountName))]);
    }

    /// <summary>明確分享給我、目前已發布的網站。</summary>
    public async Task<IReadOnlyList<SharedSite>> SharedWithMeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var sites = await db.Sites.AsNoTracking()
            .Where(s => s.Status == SiteStatus.Published && s.AccessMode == SiteAccessMode.SelectedUsers
                && db.SiteShares.Any(x => x.SiteId == s.Id && x.UserId == userId))
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var owners = (await users.FindManyAsync([.. sites.Select(s => s.UserId).Distinct()], cancellationToken).ConfigureAwait(false)).ToDictionary(u => u.Id);
        return [.. sites.Select(s => new SharedSite(
            s.Id,
            s.Name,
            options.Value.BaseUrl is null ? null : options.Value.UrlFor(s.Slug),
            owners.TryGetValue(s.UserId, out var owner) ? owner.DisplayName : string.Empty,
            s.UpdatedAt))];
    }

    /// <summary>
    /// 簽發私人網站的登入票據（ADR-0016 §4）：回傳 SiteHost 的兌換網址。網站不存在、未發布時 NotFound；沒有權限時 Forbidden。
    /// </summary>
    public async Task<(SiteTicketOutcome Outcome, Uri? RedirectUrl)> IssueTicketAsync(Guid viewerId, bool viewerIsAdmin, Guid siteId, string? returnPath, CancellationToken cancellationToken)
    {
        var site = await db.Sites.AsNoTracking().SingleOrDefaultAsync(s => s.Id == siteId && s.Status == SiteStatus.Published, cancellationToken).ConfigureAwait(false);
        if (site is null || options.Value.BaseUrl is null)
        {
            return (SiteTicketOutcome.NotFound, null);
        }

        var shared = await db.SiteShares.AnyAsync(s => s.SiteId == siteId && s.UserId == viewerId, cancellationToken).ConfigureAwait(false);
        if (!SiteAccessRules.CanView(site.AccessMode, site.UserId, viewerId, viewerIsAdmin, shared))
        {
            return (SiteTicketOutcome.Forbidden, null);
        }

        var now = timeProvider.GetUtcNow();
        // 順手清掉過期的票據，資料表不會一直長大。
        await db.SiteTickets.Where(t => t.ExpiresAt < now.AddHours(-1)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        var ticket = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        db.SiteTickets.Add(SiteTicket.Create(siteId, viewerId, SiteAccessRules.HashTicket(ticket), now));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var target = new Uri(options.Value.UrlFor(site.Slug), ".ymir/auth");
        return (SiteTicketOutcome.Issued, new Uri($"{target}?ticket={Uri.EscapeDataString(ticket)}&path={Uri.EscapeDataString(SiteAccessRules.SafeReturnPath(returnPath))}"));
    }
}
