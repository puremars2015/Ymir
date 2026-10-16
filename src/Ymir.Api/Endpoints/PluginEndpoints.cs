using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.Platform.Identity;
using Ymir.VibeMaker.Application.Plugins;

namespace Ymir.Api.Endpoints;

/// <summary>
/// 外掛市集（ADR-0030 §1、§3）：只讀寫目前使用者自己的外掛開關；可用的外掛由管理員的設定決定。
/// 回應不含任何憑證或後端位址。
/// </summary>
internal static class PluginEndpoints
{
    public static IEndpointRouteBuilder MapPluginEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/plugins").WithTags("Plugins").RequireAntiforgeryHeader();
        group.MapGet("/", ListAsync).WithName("ListPlugins");
        group.MapPut("/{key}", SetEnabledAsync).WithName("SetPluginEnabled").Produces<PluginResponse>();
        return endpoints;
    }

    private static async Task<IReadOnlyList<PluginResponse>> ListAsync(ICurrentUser currentUser, PluginService plugins, CancellationToken cancellationToken) =>
        [.. (await plugins.ListAsync(currentUser.UserId, cancellationToken)).Select(PluginResponse.From)];

    private static async Task<IResult> SetEnabledAsync(
        string key,
        SetPluginEnabledRequest request,
        ICurrentUser currentUser,
        PluginService plugins,
        CancellationToken cancellationToken)
    {
        if (request.Enabled is not { } enabled)
        {
            return ApiProblem.Create(StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "請指定是否啟用。");
        }

        var (result, plugin) = await plugins.SetEnabledAsync(currentUser.UserId, key, enabled, currentUser.ActorName, cancellationToken);
        return result switch
        {
            PluginUpdateResult.Updated => TypedResults.Ok(PluginResponse.From(plugin!)),
            PluginUpdateResult.NotAllowed => ApiProblem.Create(StatusCodes.Status403Forbidden, "PLUGIN_NOT_ALLOWED", "管理員尚未開放這個外掛。"),
            _ => TypedResults.NotFound(),
        };
    }
}

public sealed record SetPluginEnabledRequest(bool? Enabled);

/// <summary>外掛市集的卡片（ADR-0030 §1）。</summary>
/// <param name="Key"><c>onedrive</c> 或 <c>mcp:&lt;服務名稱&gt;</c>。</param>
/// <param name="Allowed">管理員是否允許。</param>
/// <param name="Installed">OneDrive：已連結帳號；平台 MCP：一律為 true。</param>
/// <param name="Enabled">使用者自己的開關。</param>
/// <param name="AgentReady">下一次執行時 Agent 是否看得到這個外掛。</param>
/// <param name="Notice">給使用者看的限制說明。</param>
public sealed record PluginResponse(
    string Key,
    PluginKind Kind,
    string Name,
    string Description,
    bool Allowed,
    bool Installed,
    bool Enabled,
    bool AgentReady,
    string? Notice)
{
    internal static PluginResponse From(PluginInfo plugin) => new(
        plugin.Key, plugin.Kind, plugin.Name, plugin.Description, plugin.Allowed, plugin.Installed, plugin.Enabled, plugin.AgentReady, plugin.Notice);
}
