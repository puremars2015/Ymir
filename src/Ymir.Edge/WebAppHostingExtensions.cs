using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace Ymir.Edge;

/// <summary>
/// 由 API 直接提供 Angular build（同源部署，ADR-0002 / ADR-0006），讓 tunnel 只需要一條 ingress 規則。
/// 設定 <c>Ymir:Web:RootPath</c> 指向 <c>web/dist/ymir-web/browser</c>；未設定時不啟用（開發期由 ng serve 提供），設定了卻找不到 index.html 則拒絕啟動。
/// </summary>
public static class WebAppHostingExtensions
{
    public const string RootPathKey = "Ymir:Web:RootPath";

    /// <summary>需放在 <c>UseAuthentication</c> 之前：前端靜態檔不含機敏資料，匿名即可下載。</summary>
    public static WebApplication UseYmirWebApp(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var rootPath = app.Configuration[RootPathKey];
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return app;
        }

        var fullPath = Path.GetFullPath(rootPath, app.Environment.ContentRootPath);
        if (!File.Exists(Path.Combine(fullPath, "index.html")))
        {
            throw new InvalidOperationException($"{RootPathKey} does not contain index.html; run 'npm run build' in web/ first.");
        }

        var files = new StaticFileOptions { FileProvider = new PhysicalFileProvider(fullPath) };
        app.UseStaticFiles(files);

        // Angular 的前端路由（/workspaces/...）都回 index.html；/api 底下沒有對應的端點時維持 404，不要回 HTML。
        app.MapFallbackToFile("{*path:nonfile}", "index.html", files).AllowAnonymous();
        app.MapFallback("/api/{**path}", () => Results.NotFound());
        return app;
    }
}
