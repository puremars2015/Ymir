using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Infrastructure.Runtime.Remote;

namespace Ymir.RuntimeHost;

/// <summary>
/// Runtime host 的端點（ADR-0008）。唯一的輸入是 user id（Guid）、對外連線模式的 enum（ADR-0012 A.8）與 runtime 內要執行的程序；
/// 刻意不提供任何接受 host 路徑、image、掛載或資源設定的端點（SA §12）。
/// </summary>
internal static class RuntimeEndpoints
{
    public static void MapRuntimeEndpoints(this IEndpointRouteBuilder app)
    {
        var runtime = app.MapGroup("/v1/users/{userId:guid}/runtime").AddEndpointFilter(async (context, next) =>
            context.HttpContext.Request.RouteValues.TryGetValue("userId", out var value)
            && Guid.TryParse(value?.ToString(), out var userId)
            && userId != Guid.Empty
                ? await next(context)
                : Results.BadRequest());

        // network 只接受 enum（internet / restricted）；受限網路的名稱只來自 runtime host 自己的設定（ADR-0012 A.8）。
        runtime.MapPost("/", async (Guid userId, string? network, RuntimeRegistry registry, CancellationToken cancellationToken) =>
        {
            if (!RuntimeHostProtocol.TryParseNetwork(network, out var access))
            {
                return Results.BadRequest();
            }

            try
            {
                return Results.Ok(RuntimeInfoMessage.From(await registry.EnsureAsync(userId, access, cancellationToken)));
            }
            catch (RuntimeNetworkUnavailableException)
            {
                return Results.Conflict();
            }
        });

        // 查詢與停止以 user id 直接操作 container，不依賴 registry：runtime host 重新啟動後仍可停止既有的 container。
        runtime.MapGet("/", async (Guid userId, RuntimeRegistry registry, CancellationToken cancellationToken) =>
            Results.Ok(new RuntimeStateMessage(await registry.Manager.GetStatusForUserAsync(userId, cancellationToken))));

        runtime.MapPost("/start", async (Guid userId, RuntimeRegistry registry, CancellationToken cancellationToken) =>
        {
            if (registry.Find(userId) is not { } runtimeId)
            {
                return Results.NotFound();
            }

            await registry.Manager.StartAsync(runtimeId, cancellationToken);
            return Results.Ok(RuntimeInfoMessage.From(await registry.Manager.GetStatusAsync(runtimeId, cancellationToken)));
        });

        runtime.MapPost("/stop", async (Guid userId, RuntimeRegistry registry, CancellationToken cancellationToken) =>
            await registry.Manager.StopForUserAsync(userId, cancellationToken) ? Results.NoContent() : Results.NotFound());

        runtime.MapDelete("/", async (Guid userId, RuntimeRegistry registry, CancellationToken cancellationToken) =>
        {
            if (registry.Find(userId) is not { } runtimeId)
            {
                return Results.NotFound();
            }

            await registry.Manager.DeleteAsync(runtimeId, cancellationToken);
            registry.Forget(userId);
            return Results.NoContent();
        });

        runtime.MapGet("/process", async (Guid userId, HttpContext context, RuntimeRegistry registry, ProcessBridge bridge) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                return Results.BadRequest();
            }

            var runtimeId = await registry.ResolveForProcessAsync(userId, context.RequestAborted);
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await bridge.RunAsync(socket, runtimeId, context.RequestAborted);
            return Results.Empty;
        });
    }
}
