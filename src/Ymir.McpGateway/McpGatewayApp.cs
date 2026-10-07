using System.Threading.RateLimiting;
using Ymir.Platform.Infrastructure;
using Ymir.VibeMaker.Application.PlatformMcp;

namespace Ymir.McpGateway;

/// <summary><c>McpGateway</c> 設定（ADR-0012 B）。</summary>
public sealed class McpGatewayOptions
{
    /// <summary>服務目錄 <c>deploy/mcp/servers.json</c>（與 API 相同的檔案）。</summary>
    public string? CatalogPath { get; set; }

    /// <summary>與 API 共用的 token 簽章金鑰；只放部署 secret。</summary>
    public string? TokenSigningKey { get; set; }

    /// <summary>每位使用者每分鐘的請求上限。</summary>
    public int RequestsPerMinute { get; set; } = 60;

    /// <summary>單一 POST 請求等待後端回應的上限。</summary>
    public int BackendTimeoutSeconds { get; set; } = 120;

    /// <summary>單一請求本文上限（JSON-RPC 訊息）。</summary>
    public int MaxRequestBytes { get; set; } = 1024 * 1024;
}

/// <summary>
/// Ymir MCP Gateway（ADR-0012 B）：Agent 連平台服務的唯一入口。
/// <list type="bullet">
/// <item>每個請求驗證 API 簽發的每人短期 token（使用者、允許的服務、期限），不接受 cookie，不暴露給瀏覽器。</item>
/// <item>依目錄把 <c>/mcp/{server}</c> 轉送到後端；後端憑證只在 gateway 的部署 secret（環境變數），不進 Agent container。</item>
/// <item>每人 rate limit、逾時；每次工具呼叫寫稽核（不含參數與回傳內容）。</item>
/// <item>不掛載 container runtime socket 或使用者 workspace，也不讀 Ymir 的業務資料表；只用資料庫寫稽核。</item>
/// </list>
/// </summary>
public static class McpGatewayApp
{
    public const string SectionName = "McpGateway";
    public const string RateLimitPolicy = "per-user";

    public static WebApplication Build(string[] args) => Build(args, configureServices: null);

    /// <param name="configureServices">測試用：在預設註冊之後替換服務（例如 TimeProvider）。</param>
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configureServices)
    {
        var builder = WebApplication.CreateBuilder(args);
        configureServices?.Invoke(builder);
        var options = builder.Configuration.GetSection(SectionName).Get<McpGatewayOptions>() ?? new McpGatewayOptions();
        McpGatewayToken.EnsureKeyIsStrong(options.TokenSigningKey, $"{SectionName}:TokenSigningKey");
        if (string.IsNullOrWhiteSpace(options.CatalogPath))
        {
            throw new InvalidOperationException($"{SectionName}:CatalogPath is required.");
        }

        var connectionString = builder.Configuration.GetConnectionString("ymir")
            ?? throw new InvalidOperationException("ConnectionStrings:ymir is required (audit log).");

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(McpCatalog.Load(options.CatalogPath));
        builder.Services.AddPlatformInfrastructure(connectionString);
        builder.Services.AddDataProtection();
        builder.Services.AddHttpClient(McpProxy.BackendClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        builder.Services.AddSingleton<McpProxy>();
        builder.Services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                GatewayAuthentication.ClaimsOf(context)?.UserId.ToString("D") ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Max(1, options.RequestsPerMinute),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        });

        var app = builder.Build();
        app.UseMiddleware<GatewayAuthentication>();
        app.UseRateLimiter();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapMethods("/mcp/{server}", ["POST", "GET", "DELETE"], (string server, HttpContext context, McpProxy proxy) => proxy.ForwardAsync(server, context))
            .RequireRateLimiting(RateLimitPolicy);
        return app;
    }
}
