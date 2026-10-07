using Microsoft.EntityFrameworkCore;
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
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<SiteRequestHandler>();

        var app = builder.Build();
        app.MapGet("/.ymir/health", () => Results.Ok(new { status = "ok" }));
        app.Run(context => context.RequestServices.GetRequiredService<SiteRequestHandler>().HandleAsync(context));
        return app;
    }
}
