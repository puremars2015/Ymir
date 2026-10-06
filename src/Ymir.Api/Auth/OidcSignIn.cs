using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Ymir.Platform.Auditing;
using Ymir.Platform.Infrastructure.Identity;
using Ymir.Platform.Users;

namespace Ymir.Api.Auth;

/// <summary>
/// 企業帳號（Entra ID）OIDC 登入（ADR-0009）：Authorization Code + PKCE，由後端換 token；
/// 瀏覽器只拿到 Ymir 自己的 cookie，IdP token 不保存（ADR-0002）。
/// </summary>
internal static class OidcSignIn
{
    public const string CallbackPath = "/signin-oidc";

    public static void Configure(OpenIdConnectOptions options, OidcLoginOptions settings, IHostEnvironment environment)
    {
        options.Authority = settings.Authority;
        options.ClientId = settings.ClientId;
        options.ClientSecret = settings.ClientSecret;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        // query 而非 form_post：callback 是一般的 GET 導向，cookie 用 SameSite=Lax 即可，http://localhost 開發也能用。
        options.ResponseMode = OpenIdConnectResponseMode.Query;
        options.CallbackPath = CallbackPath;
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.SaveTokens = false;
        options.MapInboundClaims = false;
        options.GetClaimsFromUserInfoEndpoint = false;
        options.RequireHttpsMetadata = !environment.IsDevelopment();
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.TokenValidationParameters.NameClaimType = "name";
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.NonceCookie.SameSite = SameSiteMode.Lax;
        var secure = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.CorrelationCookie.SecurePolicy = secure;
        options.NonceCookie.SecurePolicy = secure;

        options.Events.OnTokenValidated = OnTokenValidatedAsync;
        options.Events.OnRemoteFailure = OnRemoteFailureAsync;
        options.Events.OnAccessDenied = context =>
        {
            context.HandleResponse();
            context.Response.Redirect("/login?error=failed");
            return Task.CompletedTask;
        };
    }

    /// <summary>id_token 驗證通過：upsert 使用者、同步角色，把 principal 換成 Ymir 自己的 claims。</summary>
    private static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        var services = context.HttpContext.RequestServices;
        var provider = services.GetRequiredService<EntraIdentityProvider>();
        var users = services.GetRequiredService<IUserDirectory>();
        var auditLog = services.GetRequiredService<IAuditLog>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var cancellationToken = context.HttpContext.RequestAborted;

        var identity = context.Principal is null ? null : provider.Resolve(context.Principal);
        if (identity is null)
        {
            context.Fail("The id_token does not contain the required issuer / subject claims.");
            return;
        }

        var user = await users.UpsertOnLoginAsync(identity, provider.ResolveRole(context.Principal!), cancellationToken, roleManagedByIdentityProvider: true);
        if (user.Status == UserStatus.Disabled)
        {
            await auditLog.WriteAsync(new AuditEntry($"user:{user.Id:D}", "auth.login.oidc", "user", user.Id.ToString("D"), AuditResult.Denied, now, null), cancellationToken);
            context.HandleResponse();
            context.Response.Redirect("/login?error=disabled");
            return;
        }

        context.Principal = YmirClaims.CreatePrincipal(user, CookieAuthenticationDefaults.AuthenticationScheme);
        await auditLog.WriteAsync(new AuditEntry($"user:{user.Id:D}", "auth.login.oidc", "user", user.Id.ToString("D"), AuditResult.Success, now, null), cancellationToken);
    }

    /// <summary>state / nonce / token 驗證失敗等：細節只寫 server log，瀏覽器只看到「登入失敗」（SA §12）。</summary>
    private static Task OnRemoteFailureAsync(RemoteFailureContext context)
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(OidcSignIn));
        logger.LogWarning(context.Failure, "OIDC sign-in failed");
        context.HandleResponse();
        context.Response.Redirect("/login?error=failed");
        return Task.CompletedTask;
    }
}
