using Ymir.VibeMaker.Infrastructure.Runtime.Remote;

namespace Ymir.RuntimeHost;

/// <summary>
/// Cloudflare Tunnel 管理端點（ADR-0010）：只接受 token 字串。檔案位置與服務名稱來自 runtime host 的設定，
/// 不接受路徑、指令或其他參數（CLAUDE.md 安全紅線）。
/// </summary>
internal static class EdgeEndpoints
{
    public static void MapEdgeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(RuntimeHostProtocol.TunnelPath, async (TunnelManager tunnel, CancellationToken cancellationToken) =>
            Results.Ok(await tunnel.GetStatusAsync(cancellationToken)));

        app.MapPut(RuntimeHostProtocol.TunnelTokenPath, async (TunnelTokenMessage? message, TunnelManager tunnel, CancellationToken cancellationToken) =>
            await tunnel.SetTokenAsync(message?.Token, cancellationToken) switch
            {
                TunnelSetResult.Success => Results.NoContent(),
                TunnelSetResult.Disabled => Results.Conflict(),
                TunnelSetResult.Invalid => Results.BadRequest(),
                _ => Results.StatusCode(StatusCodes.Status502BadGateway),
            });
    }
}
