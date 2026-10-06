using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Ymir.Edge;

/// <summary>
/// 經由 Cloudflare Tunnel 對外公開 Ymir 平台時的 API 端設定（ADR-0006）：
/// 只信任 cloudflared 的 <c>X-Forwarded-*</c>、限制 Host header、HSTS，並拒絕在 Development 環境對外。
/// </summary>
public static class PublicEdgeExtensions
{
    private static readonly string[] LoopbackProxies = ["127.0.0.1", "::1"];

    public static IServiceCollection AddPublicEdge(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var options = configuration.GetSection(PublicEdgeOptions.SectionName).Get<PublicEdgeOptions>() ?? new PublicEdgeOptions();
        services.AddSingleton(options);
        if (!options.Enabled)
        {
            return services;
        }

        // Development 有免密碼的 /api/dev/login（任何人都能以 Admin 登入）與沒有隔離的 Local runtime，
        // 對外等於把主機交給網際網路上的任何人，因此直接拒絕啟動（ADR-0006）。
        if (environment.IsDevelopment())
        {
            throw new InvalidOperationException("Ymir:PublicEdge is not allowed in the Development environment (dev login and Local runtime must never be public).");
        }

        var hostname = options.PublicHostname?.Trim();
        if (!PublicEdgeHostnames.IsValid(hostname))
        {
            throw new InvalidOperationException("Ymir:PublicEdge:PublicHostname must be a DNS host name without scheme or port, e.g. ymir.example.com.");
        }

        options.PublicHostname = hostname;

        var proxies = (options.KnownProxies.Count > 0 ? options.KnownProxies : LoopbackProxies)
            .Select(ParseProxy)
            .ToList();

        services.Configure<ForwardedHeadersOptions>(forwarded =>
        {
            forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // cloudflared 是唯一一層 proxy；只取最後一個值，其餘由用戶端偽造的值一律忽略。
            forwarded.ForwardLimit = 1;
            forwarded.KnownIPNetworks.Clear();
            forwarded.KnownProxies.Clear();
            foreach (var proxy in proxies)
            {
                forwarded.KnownProxies.Add(proxy);
            }
        });

        // 對外網域可由管理介面修改（ADR-0010）：Host 限制改在 UsePublicEdge 的 middleware 每次讀目前值，
        // 不再使用啟動時固定的 HostFilteringOptions。沒有覆寫時使用部署設定。
        services.TryAddSingleton<IPublicHostnameSource, DeploymentPublicHostname>();

        services.AddHsts(hsts => hsts.MaxAge = TimeSpan.FromDays(180));

        return services;
    }

    /// <summary>必須是 pipeline 的第一個 middleware，後面的 cookie Secure、XSRF、稽核才會看到正確的 scheme 與用戶端 IP。</summary>
    public static WebApplication UsePublicEdge(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var options = app.Services.GetRequiredService<PublicEdgeOptions>();
        if (!options.Enabled)
        {
            return app;
        }

        app.UseForwardedHeaders();
        app.UseHsts();

        var source = app.Services.GetRequiredService<IPublicHostnameSource>();
        app.Use(async (context, next) =>
        {
            var publicHostname = await source.GetOverrideAsync(context.RequestAborted) ?? options.PublicHostname!;
            var host = context.Request.Host.Host;

            // 只接受公開網域與本機（health check、本機管理）；擋掉直接以其他網域（例如 *.trycloudflare.com）連進來的請求。
            if (!string.Equals(host, publicHostname, StringComparison.OrdinalIgnoreCase) && !IsLocalHost(host))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            // health check 只給本機監控使用。Token 模式的 tunnel 由 Cloudflare dashboard 管理 ingress，無法保證有擋 /health，
            // 因此在 API 端擋：經由公開網域進來的 /health、/alive 一律 404（ADR-0006）。
            if (IsHealthPath(context.Request.Path) && string.Equals(host, publicHostname, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next(context);
        });
        return app;
    }

    private static bool IsLocalHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host is "127.0.0.1" or "[::1]" or "::1";

    private static bool IsHealthPath(PathString path) =>
        path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/alive", StringComparison.OrdinalIgnoreCase);

    private static IPAddress ParseProxy(string value) =>
        IPAddress.TryParse(value.Trim(), out var address)
            ? address
            : throw new InvalidOperationException($"Ymir:PublicEdge:KnownProxies contains an invalid IP address: '{value}'.");
}
