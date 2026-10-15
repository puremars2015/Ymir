using Ymir.Platform.Identity;
using Ymir.VibeMaker.Application.Sites;

namespace Ymir.Api.Endpoints;

/// <summary>
/// 左側「小工具」（ADR-0029）：自己用 <c>/make</c> 小工具主題做出、已自動發布的前端頁面。
/// 只列目前使用者自己的網站；開啟時前端以 <c>POST /api/sites/{id}/ticket</c> 取得網站網域的網址，在 iframe 內顯示（內容在網站網域執行，不在 Ymir 網域）。
/// </summary>
internal static class ToolEndpoints
{
    public static IEndpointRouteBuilder MapToolEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/tools", ListAsync).WithName("ListTools").WithTags("Tools");
        return endpoints;
    }

    private static async Task<ToolsResponse> ListAsync(ICurrentUser currentUser, SiteService sites, CancellationToken cancellationToken)
    {
        var tools = sites.IsEnabled ? await sites.ListToolsAsync(currentUser.UserId, cancellationToken) : [];
        return new(sites.IsEnabled, [.. tools.Select(t => new ToolResponse(t.Site.Id, t.Site.Name, t.Site.ConversationId, t.Site.UpdatedAt))]);
    }
}

/// <param name="Enabled">網站託管是否已設定；未設定時小工具無法發布與開啟。</param>
public sealed record ToolsResponse(bool Enabled, IReadOnlyList<ToolResponse> Tools);

/// <param name="Id">網站 id（開啟時用來取得票據）。</param>
/// <param name="ConversationId">做出這個小工具的對話。</param>
public sealed record ToolResponse(Guid Id, string Name, Guid ConversationId, DateTimeOffset UpdatedAt);
