using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.Platform.Identity;
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
        return endpoints;
    }

    private static async Task<SitesResponse> ListAsync(ICurrentUser currentUser, SiteService sites, CancellationToken cancellationToken) =>
        new(sites.IsEnabled, sites.Options.MaxSitesPerUser, [.. (await sites.ListAsync(currentUser.UserId, cancellationToken)).Select(SiteResponse.From)]);

    private static async Task<IResult> CreateAsync(
        Guid conversationId,
        PublishSiteRequest request,
        ICurrentUser currentUser,
        SiteService sites,
        CancellationToken cancellationToken)
    {
        var (site, error, detail) = await sites.CreateAsync(currentUser.UserId, conversationId, request.Name, request.SourcePath, request.SpaMode, currentUser.ActorName, cancellationToken);
        return site is not null ? TypedResults.Created((string?)null, SiteResponse.From(site)) : Problem(error, detail, sites.Options);
    }

    private static async Task<IResult> RepublishAsync(
        Guid siteId,
        RepublishSiteRequest request,
        ICurrentUser currentUser,
        SiteService sites,
        CancellationToken cancellationToken)
    {
        var (site, error, detail) = await sites.RepublishAsync(currentUser.UserId, siteId, request.ConversationId, request.SourcePath, request.SpaMode, currentUser.ActorName, cancellationToken);
        return error == SitePublishError.None && site is not null ? TypedResults.Ok(SiteResponse.From(site)) : Problem(error, detail, sites.Options);
    }

    private static async Task<IResult> UnpublishAsync(Guid siteId, ICurrentUser currentUser, SiteService sites, CancellationToken cancellationToken) =>
        await sites.UnpublishAsync(currentUser.UserId, siteId, currentUser.ActorName, cancellationToken) is { } site
            ? TypedResults.Ok(SiteResponse.From(site))
            : TypedResults.NotFound();

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

/// <param name="Url">網站網址；網站託管停用時為 null。</param>
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
    DateTimeOffset UpdatedAt)
{
    internal static SiteResponse From(SiteSummary summary)
    {
        var site = summary.Site;
        return new(site.Id, site.Name, summary.Url, site.Status, site.AccessMode, site.SpaMode, site.ConversationId, site.SourcePath,
            summary.CurrentVersion?.FileCount ?? 0, summary.CurrentVersion?.TotalBytes ?? 0, site.PublishedAt, site.UpdatedAt);
    }
}
