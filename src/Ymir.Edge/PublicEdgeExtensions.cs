using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        if (string.IsNullOrEmpty(hostname) || Uri.CheckHostName(hostname) != UriHostNameType.Dns)
        {
            throw new InvalidOperationException("Ymir:PublicEdge:PublicHostname must be a DNS host name without scheme or port, e.g. ymir.example.com.");
        }

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

        // 預設的 HostFiltering middleware 讀取 AllowedHosts（appsettings 為 *）；對外時只接受公開主機名稱與本機（health check）。
        services.PostConfigure<HostFilteringOptions>(filtering =>
            filtering.AllowedHosts = [hostname, "localhost", "127.0.0.1", "[::1]"]);

        services.AddHsts(hsts => hsts.MaxAge = TimeSpan.FromDays(180));

        return services;
    }

    /// <summary>必須是 pipeline 的第一個 middleware，後面的 cookie Secure、XSRF、稽核才會看到正確的 scheme 與用戶端 IP。</summary>
    public static WebApplication UsePublicEdge(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!app.Services.GetRequiredService<PublicEdgeOptions>().Enabled)
        {
            return app;
        }

        app.UseForwardedHeaders();
        app.UseHsts();
        return app;
    }

    private static IPAddress ParseProxy(string value) =>
        IPAddress.TryParse(value.Trim(), out var address)
            ? address
            : throw new InvalidOperationException($"Ymir:PublicEdge:KnownProxies contains an invalid IP address: '{value}'.");
}
