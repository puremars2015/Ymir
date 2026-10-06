using Ymir.VibeMaker.Infrastructure.Runtime.Remote;

namespace Ymir.RuntimeHost;

/// <summary>
/// Runtime host 的端點（ADR-0008）。唯一的輸入是 user id（Guid）與 runtime 內要執行的程序；
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

        runtime.MapPost("/", async (Guid userId, RuntimeRegistry registry, CancellationToken cancellationToken) =>
            Results.Ok(RuntimeInfoMessage.From(await registry.EnsureAsync(userId, cancellationToken))));

        runtime.MapGet("/", async (Guid userId, RuntimeRegistry registry, CancellationToken cancellationToken) =>
            registry.Find(userId) is { } runtimeId
                ? Results.Ok(RuntimeInfoMessage.From(await registry.Manager.GetStatusAsync(runtimeId, cancellationToken)))
                : Results.NotFound());

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
        {
            if (registry.Find(userId) is not { } runtimeId)
            {
                return Results.NotFound();
            }

            await registry.Manager.StopAsync(runtimeId, cancellationToken);
            return Results.Ok(RuntimeInfoMessage.From(await registry.Manager.GetStatusAsync(runtimeId, cancellationToken)));
        });

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
