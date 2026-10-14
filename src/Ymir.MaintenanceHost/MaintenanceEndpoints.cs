using Ymir.Maintenance;

namespace Ymir.MaintenanceHost;

/// <summary>
/// 維護服務的端點（ADR-0025 §2）：只有固定的查詢、政策同步、紀錄查詢與 dry run。
/// 刻意沒有 shell / exec / 任意刪除端點，也沒有任何接受路徑、store、image 或 container 參數的端點；
/// 唯一的參數是紀錄查詢的數字游標 <c>after</c>。
/// </summary>
internal static class MaintenanceEndpoints
{
    public static void MapMaintenanceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(MaintenanceProtocol.ResourcesPath, (MaintenanceService service) => Results.Json(service.GetSnapshot(), MaintenanceProtocol.JsonOptions));

        app.MapGet(MaintenanceProtocol.PolicyPath, (MaintenanceService service) => Results.Json(service.Policy, MaintenanceProtocol.JsonOptions));

        app.MapPut(MaintenanceProtocol.PolicyPath, async (HttpContext context, MaintenanceService service) =>
        {
            ReclamationPolicy? policy;
            try
            {
                policy = await context.Request.ReadFromJsonAsync<ReclamationPolicy>(MaintenanceProtocol.JsonOptions, context.RequestAborted);
            }
            catch (System.Text.Json.JsonException)
            {
                return Results.BadRequest();
            }

            if (policy is null || policy.Validate() is not null)
            {
                return Results.BadRequest();
            }

            service.SetPolicy(policy);
            return Results.Json(service.Policy, MaintenanceProtocol.JsonOptions);
        });

        app.MapGet(MaintenanceProtocol.RunsPath, (long? after, MaintenanceService service) =>
            after is < 0 ? Results.BadRequest() : Results.Json(service.GetRuns(after ?? 0), MaintenanceProtocol.JsonOptions));

        app.MapPost(MaintenanceProtocol.DryRunPath, async (MaintenanceService service, CancellationToken cancellationToken) =>
            Results.Json(await service.DryRunAsync(cancellationToken), MaintenanceProtocol.JsonOptions));
    }
}

/// <summary>每個請求都要 <c>Authorization: Bearer &lt;token&gt;</c>；只有 <c>/health</c> 例外（不含任何資訊）。Socket 權限是第一道防線，token 是第二道。</summary>
internal sealed class MaintenanceTokenMiddleware(RequestDelegate next, MaintenanceHostOptions options, ILogger<MaintenanceTokenMiddleware> logger)
{
    private const string Prefix = MaintenanceProtocol.AuthorizationScheme + " ";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.Equals("/health", StringComparison.Ordinal))
        {
            await next(context);
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();
        var presented = header.StartsWith(Prefix, StringComparison.Ordinal) ? header[Prefix.Length..] : null;
        if (!MaintenanceProtocol.TokenMatches(options.Token!, presented))
        {
            logger.LogWarning("Rejected maintenance request without a valid token: {Method} {Path}", context.Request.Method, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }
}
