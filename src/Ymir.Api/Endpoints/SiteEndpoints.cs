using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.Platform.Identity;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Application.Sites;
using Ymir.VibeMaker.Domain;

namespace Ymir.Api.Endpoints;

/// <summary>
/// 網站託管（ADR-0016 §6）：從自己的對話發布網站、重新發布、取消發布、刪除。只能操作自己的對話與網站（別人的一律 404，SA §12）；
/// 來源只接受工作目錄內的相對目錄，不接受任何主機路徑。
/// </summary>
internal static class SiteEndpoints
{
    public static IEndpointRouteBuilder MapSiteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/conversations/{conversationId:guid}/sites", CreateAsync)
            .WithName("PublishSite").WithTags("Sites").RequireAntiforgeryHeader()
            .Produces<SiteResponse>(StatusCodes.Status201Created);
        var group = endpoints.MapGroup("/api/sites").WithTags("Sites").RequireAntiforgeryHeader();
        group.MapGet("/", ListAsync).WithName("ListSites");
        group.MapPost("/{siteId:guid}/publish", RepublishAsync).WithName("RepublishSite").Produces<SiteResponse>();
        group.MapPost("/{siteId:guid}/unpublish", UnpublishAsync).WithName("UnpublishSite").Produces<SiteResponse>();
        group.MapDelete("/{siteId:guid}", DeleteAsync).WithName("DeleteSite");
        group.MapPut("/{siteId:guid}/access", SetAccessAsync).WithName("SetSiteAccess").Produces<SiteResponse>();
        group.MapGet("/shared-with-me", SharedWithMeAsync).WithName("ListSitesSharedWithMe");
        group.MapPost("/{siteId:guid}/ticket", IssueTicketAsync).WithName("IssueSiteTicket").Produces<SiteTicketResponse>();

        // 分享選擇器用（ADR-0016 §3）：只回未停用帳號的 id、顯示名稱與帳號，不回 email 等其他資料。
        endpoints.MapGet("/api/users/search", SearchUsersAsync).WithName("SearchUsers").WithTags("Sites");
        return endpoints;
    }

    private static async Task<SitesResponse> ListAsync(ICurrentUser currentUser, SiteService sites, SiteAccessService access, CancellationToken cancellationToken)
    {
        var list = await sites.ListAsync(currentUser.UserId, cancellationToken);
        var shares = await access.SharesAsync([.. list.Select(s => s.Site.Id)], cancellationToken);
        return new(sites.IsEnabled, sites.Options.MaxSitesPerUser, [.. list.Select(s => SiteResponse.From(s, shares.GetValueOrDefault(s.Site.Id)))]);
    }

    private static async Task<IResult> SetAccessAsync(
        Guid siteId,
        SiteAccessRequest request,
        ICurrentUser currentUser,
        SiteService sites,
        SiteAccessService access,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Mode))
        {
            return ApiProblem.Create(StatusCodes.Status400BadRequest, "SITE_ACCESS_INVALID", "存取模式不合法。");
        }

        var (found, error) = await access.SetAccessAsync(currentUser.UserId, siteId, request.Mode, request.UserIds ?? [], currentUser.ActorName, cancellationToken);
        if (!found)
        {
            return TypedResults.NotFound();
        }

        if (error is not null)
        {
            return ApiProblem.Create(StatusCodes.Status400BadRequest, "SITE_ACCESS_INVALID", error);
        }

        return await ToResponseAsync(currentUser.UserId, siteId, sites, access, cancellationToken) is { } response ? TypedResults.Ok(response) : TypedResults.NotFound();
    }

    private static async Task<IReadOnlyList<SharedSiteResponse>> SharedWithMeAsync(ICurrentUser currentUser, SiteAccessService access, CancellationToken cancellationToken) =>
        [.. (await access.SharedWithMeAsync(currentUser.UserId, cancellationToken)).Select(s => new SharedSiteResponse(s.Id, s.Name, s.Url, s.OwnerName, s.UpdatedAt))];

    /// <summary>
    /// 私人網站的登入票據（ADR-0016 §4）：SiteHost 把未登入的瀏覽者導到平台的 <c>/site-access</c> 頁，那一頁以 Ymir cookie 呼叫這裡，
    /// 再把瀏覽器導向回傳的 SiteHost 兌換網址。用 POST + antiforgery，避免別的網站以 GET 觸發簽發。
    /// </summary>
    private static async Task<IResult> IssueTicketAsync(
        Guid siteId,
        SiteTicketRequest request,
        ICurrentUser currentUser,
        SiteAccessService access,
        CancellationToken cancellationToken)
    {
        var (outcome, url) = await access.IssueTicketAsync(currentUser.UserId, currentUser.Role == UserRole.Admin, siteId, request.Path, cancellationToken);
        return outcome switch
        {
            SiteTicketOutcome.Issued when url is not null => TypedResults.Ok(new SiteTicketResponse(url)),
            SiteTicketOutcome.Forbidden => ApiProblem.Create(StatusCodes.Status403Forbidden, "SITE_FORBIDDEN", "網站擁有者沒有分享這個網站給你。"),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<IReadOnlyList<UserSearchResult>> SearchUsersAsync(string? q, ICurrentUser currentUser, IUserDirectory users, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length > 100)
        {
            return [];
        }

        var found = await users.ListAsync(q, 50, cancellationToken);
        return [.. found.Where(u => u.Status == UserStatus.Active && u.Id != currentUser.UserId).Take(20).Select(u => new UserSearchResult(u.Id, u.DisplayName, u.AccountName))];
    }

    private static async Task<SiteResponse?> ToResponseAsync(Guid userId, Guid siteId, SiteService sites, SiteAccessService access, CancellationToken cancellationToken)
    {
        var summary = (await sites.ListAsync(userId, cancellationToken)).FirstOrDefault(s => s.Site.Id == siteId);
        if (summary is null)
        {
            return null;
        }

        var shares = await access.SharesAsync([siteId], cancellationToken);
        return SiteResponse.From(summary, shares.GetValueOrDefault(siteId));
    }

    private static async Task<IResult> CreateAsync(
        Guid conversationId,
        PublishSiteRequest request,
        ICurrentUser currentUser,
        SiteService sites,
        CancellationToken cancellationToken)
    {
        var (site, error, detail) = await sites.CreateAsync(currentUser.UserId, conversationId, request.Name, request.SourcePath, request.SpaMode, currentUser.ActorName, cancellationToken);
        return site is not null ? TypedResults.Created((string?)null, SiteResponse.From(site, null)) : Problem(error, detail, sites.Options);
    }

    private static async Task<IResult> RepublishAsync(
        Guid siteId,
        RepublishSiteRequest request,
        ICurrentUser currentUser,
        SiteService sites,
        SiteAccessService access,
        CancellationToken cancellationToken)
    {
        var (site, error, detail) = await sites.RepublishAsync(currentUser.UserId, siteId, request.ConversationId, request.SourcePath, request.SpaMode, currentUser.ActorName, cancellationToken);
        if (error != SitePublishError.None || site is null)
        {
            return Problem(error, detail, sites.Options);
        }

        var shares = await access.SharesAsync([siteId], cancellationToken);
        return TypedResults.Ok(SiteResponse.From(site, shares.GetValueOrDefault(siteId)));
    }

    private static async Task<IResult> UnpublishAsync(Guid siteId, ICurrentUser currentUser, SiteService sites, SiteAccessService access, CancellationToken cancellationToken)
    {
        if (await sites.UnpublishAsync(currentUser.UserId, siteId, currentUser.ActorName, cancellationToken) is not { } site)
        {
            return TypedResults.NotFound();
        }

        var shares = await access.SharesAsync([siteId], cancellationToken);
        return TypedResults.Ok(SiteResponse.From(site, shares.GetValueOrDefault(siteId)));
    }

    private static async Task<IResult> DeleteAsync(Guid siteId, ICurrentUser currentUser, SiteService sites, CancellationToken cancellationToken) =>
        await sites.DeleteAsync(currentUser.UserId, siteId, currentUser.ActorName, cancellationToken) ? TypedResults.NoContent() : TypedResults.NotFound();

    private static IResult Problem(SitePublishError error, string? detail, SiteOptions options) => error switch
    {
        SitePublishError.NotFound => TypedResults.NotFound(),
        SitePublishError.Disabled => ApiProblem.Create(StatusCodes.Status409Conflict, "SITES_DISABLED", "網站託管尚未設定，請洽管理員。"),
        SitePublishError.TooManySites => ApiProblem.Create(StatusCodes.Status409Conflict, "SITE_LIMIT", $"每人最多 {options.MaxSitesPerUser} 個網站。"),
        SitePublishError.InvalidSource => ApiProblem.Create(StatusCodes.Status400BadRequest, "SITE_SOURCE_INVALID", "來源目錄或網站名稱不合法。"),
        SitePublishError.MissingIndex => ApiProblem.Create(StatusCodes.Status400BadRequest, "SITE_INDEX_MISSING", detail ?? "來源目錄沒有 index.html。"),
        SitePublishError.TooLarge => ApiProblem.Create(StatusCodes.Status413PayloadTooLarge, "SITE_TOO_LARGE", detail ?? "網站太大。"),
        _ => ApiProblem.Create(StatusCodes.Status502BadGateway, "SITE_PUBLISH_FAILED", detail ?? "發布失敗，請稍後重試。"),
    };
}

/// <param name="SourcePath">工作目錄內的來源目錄（例如 <c>dist</c>）；空白或 <c>.</c> 表示工作目錄本身。</param>
/// <param name="SpaMode">單頁應用：沒有副檔名的路徑回 index.html。</param>
public sealed record PublishSiteRequest(string? Name, string? SourcePath, bool SpaMode = false);

/// <summary>重新發布；欄位為 null 時沿用網站目前的設定。</summary>
public sealed record RepublishSiteRequest(Guid? ConversationId, string? SourcePath, bool? SpaMode);

/// <param name="Enabled">是否設定了網站託管。</param>
public sealed record SitesResponse(bool Enabled, int MaxSites, IReadOnlyList<SiteResponse> Sites);

/// <param name="Mode">存取模式（ADR-0016 §3）；擁有者與 Ymir 管理員一律可以看。</param>
/// <param name="UserIds">指定使用者（只在 <see cref="SiteAccessMode.SelectedUsers"/> 時使用）。</param>
public sealed record SiteAccessRequest(SiteAccessMode Mode, IReadOnlyList<Guid>? UserIds);

/// <param name="Path">登入後回到網站的路徑（站內相對路徑，其他一律改成 <c>/</c>）。</param>
public sealed record SiteTicketRequest(string? Path);

/// <param name="RedirectUrl">SiteHost 的票據兌換網址（60 秒、只能用一次）。</param>
public sealed record SiteTicketResponse(Uri RedirectUrl);

public sealed record SharedSiteResponse(Guid Id, string Name, Uri? Url, string OwnerName, DateTimeOffset UpdatedAt);

public sealed record UserSearchResult(Guid Id, string DisplayName, string? AccountName);

public sealed record SiteShareResponse(Guid UserId, string DisplayName, string? AccountName);

/// <param name="Url">網站網址；網站託管停用時為 null。</param>
/// <param name="SharedWith">分享名單（只在 <see cref="SiteAccessMode.SelectedUsers"/> 時有意義）。</param>
public sealed record SiteResponse(
    Guid Id,
    string Name,
    Uri? Url,
    SiteStatus Status,
    SiteAccessMode AccessMode,
    bool SpaMode,
    Guid ConversationId,
    string SourcePath,
    int FileCount,
    long TotalBytes,
    DateTimeOffset? PublishedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<SiteShareResponse> SharedWith)
{
    internal static SiteResponse From(SiteSummary summary, IReadOnlyList<SiteShareInfo>? shares)
    {
        var site = summary.Site;
        return new(site.Id, site.Name, summary.Url, site.Status, site.AccessMode, site.SpaMode, site.ConversationId, site.SourcePath,
            summary.CurrentVersion?.FileCount ?? 0, summary.CurrentVersion?.TotalBytes ?? 0, site.PublishedAt, site.UpdatedAt,
            [.. (shares ?? []).Select(s => new SiteShareResponse(s.UserId, s.DisplayName, s.AccountName))]);
    }
}
