using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Ymir.Api.Problems;
using Ymir.Platform.Infrastructure.Identity;
using Ymir.Platform.Users;
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

    /// <summary>OIDC（企業帳號）的 authentication scheme 名稱。</summary>
    public const string OidcScheme = "oidc";

    /// <summary>Admin 專用端點的 policy（SA §4）。</summary>
    public const string AdminPolicy = "AdminOnly";

    /// <summary>帳號密碼登入的 rate limit policy（每個來源 IP）。</summary>
    public const string PasswordLoginRateLimit = "password-login";

    public static IServiceCollection AddYmirAuth(this IServiceCollection services, IHostEnvironment environment, IConfiguration configuration)
    {
        var section = configuration.GetSection(YmirAuthOptions.SectionName);
        services.Configure<YmirAuthOptions>(section);
        var authOptions = section.Get<YmirAuthOptions>() ?? new YmirAuthOptions();
        ValidateLoginMethods(authOptions, environment);
        services.AddSingleton<EntraIdentityProvider>();

        var authentication = services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
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
                // 每個請求確認帳號仍有效：Admin 停用後，既有 cookie 在下一個請求就失效（ADR-0009）。
                options.Events.OnValidatePrincipal = CookiePrincipalValidator.ValidateAsync;
            });

        if (authOptions.Oidc.IsConfigured)
        {
            authentication.AddOpenIdConnect(OidcScheme, options => OidcSignIn.Configure(options, authOptions.Oidc, environment));
        }

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(AdminPolicy, policy => policy.RequireAuthenticatedUser().RequireRole(nameof(UserRole.Admin)));

        // 帳號密碼登入：每個來源 IP 每分鐘有次數上限（預設 10），搭配帳號鎖定減緩猜密碼。
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(PasswordLoginRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, authOptions.LocalAccounts.LoginAttemptsPerMinute), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });

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

    /// <summary>
    /// 至少要有一種登入方式；非 Development 的 OIDC authority 必須是 https（ADR-0009）。
    /// Development 另外有 dev 登入，所以兩者都關閉也可以啟動。
    /// </summary>
    internal static void ValidateLoginMethods(YmirAuthOptions options, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() && !options.Oidc.IsConfigured && !options.LocalAccounts.Enabled)
        {
            throw new InvalidOperationException("No login method is configured: set Ymir:Auth:Oidc or enable Ymir:Auth:LocalAccounts (ADR-0009).");
        }

        if (options.Oidc.IsConfigured)
        {
            if (!Uri.TryCreate(options.Oidc.Authority, UriKind.Absolute, out var authority)
                || (authority.Scheme != Uri.UriSchemeHttps && !(environment.IsDevelopment() && authority.IsLoopback)))
            {
                throw new InvalidOperationException("Ymir:Auth:Oidc:Authority must be an https URL (http is allowed only for loopback in Development).");
            }

            if (string.IsNullOrWhiteSpace(options.Oidc.ClientSecret))
            {
                throw new InvalidOperationException("Ymir:Auth:Oidc:ClientSecret is required (keep it in deploy/api/.env, ADR-0009).");
            }
        }
    }

    /// <summary>
    /// 本機帳號必須先改密碼時（ADR-0009），只允許查詢自己、改密碼、登出；其他 API 一律 403。
    /// </summary>
    public static IApplicationBuilder UsePasswordChangeRequirement(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated == true
                && YmirClaims.RequiresPasswordChange(context.User)
                && context.Request.Path.StartsWithSegments("/api")
                && !IsAllowedDuringPasswordChange(context.Request))
            {
                await ApiProblem.WriteAsync(context, StatusCodes.Status403Forbidden, "PASSWORD_CHANGE_REQUIRED", "請先變更密碼。");
                return;
            }

            await next(context);
        });

    private static bool IsAllowedDuringPasswordChange(HttpRequest request) =>
        (HttpMethods.IsGet(request.Method) && (request.Path.Equals("/api/me", StringComparison.Ordinal) || request.Path.StartsWithSegments("/api/auth/providers")))
        || (HttpMethods.IsPost(request.Method) && (request.Path.Equals("/api/me/password", StringComparison.Ordinal) || request.Path.Equals("/api/auth/logout", StringComparison.Ordinal)));

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
