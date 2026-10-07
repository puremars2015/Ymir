using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Infrastructure;
using Ymir.VibeMaker.Infrastructure.Persistence;

namespace Ymir.SiteHost;

/// <summary><c>SiteHost</c> 設定（ADR-0016）。</summary>
public sealed class SiteHostOptions
{
    public const string SectionName = "SiteHost";

    /// <summary>與 API 的 <c>Ymir:Sites:BaseUrl</c> 相同；網站的 Host 必須是 <c>{slug}.{BaseUrl.Host}</c>。</summary>
    public Uri? BaseUrl { get; set; }

    /// <summary>網站 volume（唯讀）。</summary>
    public string? Root { get; set; }

    /// <summary>Ymir 前端的網址（私人網站登入時導向 <c>/site-access</c>，ADR-0016 §4）。</summary>
    public Uri? PlatformUrl { get; set; }

    /// <summary>網站 cookie 的 Data Protection 金鑰目錄（與 API 分開）。</summary>
    public string? DataProtectionKeysPath { get; set; }
}

/// <summary>
/// SiteHost（ADR-0016）：依 Host 找到網站、檢查訪客權限、提供目前版本的靜態檔案。
/// 只讀：網站 volume 唯讀、資料庫只查網站與授權；不執行使用者程式，不掛載 workspace 或 container runtime socket。
/// </summary>
public static class SiteHostApp
{
    public static WebApplication Build(string[] args) => Build(args, configure: null);

    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);
        var options = builder.Configuration.GetSection(SiteHostOptions.SectionName).Get<SiteHostOptions>() ?? new SiteHostOptions();
        if (options.BaseUrl is null || string.IsNullOrWhiteSpace(options.Root))
        {
            throw new InvalidOperationException("SiteHost:BaseUrl and SiteHost:Root are required.");
        }

        var connectionString = builder.Configuration.GetConnectionString("ymir")
            ?? throw new InvalidOperationException("ConnectionStrings:ymir is required.");
        builder.Services.AddSingleton(options);
        builder.Services.AddDbContext<VibeMakerDbContext>(db => db.UseSqlServer(connectionString, VibeMakerSqlServerOptions.Configure)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
        // 使用者狀態與角色（停用立即生效、Admin 可看所有網站，ADR-0016 §3）。
        builder.Services.AddPlatformInfrastructure(connectionString);
        // 網站 cookie 用自己的金鑰（與 API 分開）：平台的 Data Protection 金鑰不會交給 SiteHost。
        var dataProtection = builder.Services.AddDataProtection().SetApplicationName("Ymir.SiteHost");
        if (!string.IsNullOrWhiteSpace(options.DataProtectionKeysPath))
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(options.DataProtectionKeysPath));
        }

        builder.Services.AddMemoryCache();
        builder.Services.AddScoped<SiteRequestHandler>();

        var app = builder.Build();
        app.MapGet("/.ymir/health", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/.ymir/auth", (HttpContext context, SiteRequestHandler handler) => handler.HandleTicketAsync(context));
        // 用 fallback 端點而不是 app.Run：terminal middleware 會在端點執行前攔下所有請求（包含上面的 /.ymir/*）。
        // 模式不加 :nonfile，讓 app.js 等有副檔名的路徑也交給網站處理。
        app.MapFallback("{**path}", (HttpContext context, SiteRequestHandler handler) => handler.HandleAsync(context));
        return app;
    }
}
