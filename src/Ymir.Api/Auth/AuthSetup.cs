using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Ymir.Api.Problems;
using Ymir.VibeMaker.Contracts.Executions;

namespace Ymir.Api.Auth;

/// <summary>
/// BFF + HttpOnly Cookie 認證（ADR-0002）。瀏覽器只拿到 cookie，原生 EventSource 會自動帶上。
/// 所有端點預設都需要登入（fallback policy），匿名端點必須明確標示 <c>AllowAnonymous</c>。
/// </summary>
internal static class AuthSetup
{
    public const string XsrfCookieName = "XSRF-TOKEN";
    public const string XsrfHeaderName = "X-XSRF-TOKEN";

    /// <summary>
    /// Data Protection 金鑰（cookie、antiforgery 都靠它）：設定 <c>Ymir:DataProtection:KeysPath</c> 時存到該目錄。
    /// API 在容器內執行時必須設定並掛載主機目錄，否則容器重建後所有登入都會失效（ADR-0002、ADR-0008）。
    /// </summary>
    public static IServiceCollection AddYmirDataProtection(this IServiceCollection services, IConfiguration configuration)
    {
        var dataProtection = services.AddDataProtection().SetApplicationName("Ymir");
        if (configuration["Ymir:DataProtection:KeysPath"] is { Length: > 0 } keysPath)
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        return services;
    }

    public static IServiceCollection AddYmirAuth(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "ymir.auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                // API 不做 redirect，直接回 401 / 403 ProblemDetails。
                options.Events.OnRedirectToLogin = context =>
                    ApiProblem.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized, ExecutionErrorCodes.AuthRequired, "請先登入。");
                options.Events.OnRedirectToAccessDenied = context =>
                    ApiProblem.WriteAsync(context.HttpContext, StatusCodes.Status403Forbidden, ExecutionErrorCodes.Forbidden, "沒有權限。");
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddAntiforgery(options =>
        {
            options.HeaderName = XsrfHeaderName;
            options.Cookie.Name = "ymir.antiforgery";
            options.Cookie.SameSite = SameSiteMode.Strict;
        });

        return services;
    }

    /// <summary>
    /// 每個 GET /api 請求都更新 <c>XSRF-TOKEN</c> cookie（JavaScript 可讀），Angular HttpClient 會自動放到 <c>X-XSRF-TOKEN</c> header。
    /// </summary>
    public static IApplicationBuilder UseXsrfTokenCookie(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path.StartsWithSegments("/api"))
            {
                IssueXsrfToken(context);
            }

            await next(context);
        });

    public static void IssueXsrfToken(HttpContext context)
    {
        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
        var tokens = antiforgery.GetAndStoreTokens(context);
        context.Response.Cookies.Append(XsrfCookieName, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            SameSite = SameSiteMode.Strict,
            Secure = context.Request.IsHttps,
            Path = "/",
        });
    }

    /// <summary>狀態變更的端點要求有效的 antiforgery token（ADR-0002）。</summary>
    public static TBuilder RequireAntiforgeryHeader<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var httpContext = context.HttpContext;
            if (!HttpMethods.IsGet(httpContext.Request.Method) && !HttpMethods.IsHead(httpContext.Request.Method))
            {
                try
                {
                    await httpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(httpContext);
                }
                catch (AntiforgeryValidationException)
                {
                    return ApiProblem.Create(StatusCodes.Status400BadRequest, "ANTIFORGERY_INVALID", "請重新整理頁面後再試一次。");
                }
            }

            return await next(context);
        });
}
