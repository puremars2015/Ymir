using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Ymir.Platform.Auditing;
using Ymir.Platform.Identity;
using Ymir.Platform.Users;

namespace Ymir.Api.Auth;

/// <summary>
/// Google 帳號 SSO（ADR-0024）：與 Entra 相同的 Authorization Code + PKCE 流程，由後端換 token、不保存 IdP token（ADR-0002）。
/// <para>
/// Google 帳號任何人都能申請，所以只放行部署設定白名單內的帳號：Workspace 網域（<c>hd</c> claim）或個別 email，
/// 且 email 必須經 Google 驗證（<c>email_verified</c>）。不符合時不建立使用者，導回 <c>/login?error=not_allowed</c>。
/// </para>
/// </summary>
internal static class GoogleLogin
{
    public const string Scheme = "google";

    public const string CallbackPath = "/signin-google";

    /// <summary>
    /// Google 的 issuer，也是 Ymir 保存的使用者 issuer。Google 的 id_token 可能是 <c>accounts.google.com</c>（沒有 scheme），
    /// 一律存成這個值，讓同一個 Google 帳號（<c>sub</c>）只對應一個 Ymir 使用者。
    /// </summary>
    public const string Issuer = "https://accounts.google.com";

    private const string LegacyIssuer = "accounts.google.com";

    public static void Configure(OpenIdConnectOptions options, GoogleLoginOptions google, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(google);
        options.Authority = google.Authority;
        options.ClientId = google.ClientId;
        options.ClientSecret = google.ClientSecret;
        options.CallbackPath = CallbackPath;
        OidcSignIn.ApplyCommon(options, environment);
        options.TokenValidationParameters.ValidIssuers = [google.Authority.TrimEnd('/'), LegacyIssuer];
        options.Events.OnRedirectToIdentityProvider = context =>
        {
            // 授權碼本來就以 query 回傳；Google 不需要 response_mode，省略以免被當成不支援的參數。
            context.ProtocolMessage.ResponseMode = null;
            // 瀏覽器登入多個 Google 帳號時讓使用者選擇，避免直接用到不在白名單的個人帳號。
            context.ProtocolMessage.Prompt = "select_account";
            return Task.CompletedTask;
        };
        options.Events.OnTokenValidated = context => OnTokenValidatedAsync(context, google);
    }

    /// <summary>
    /// 部署設定檢查（ADR-0024）：必須有 client secret 與至少一項白名單；authority 規則與 Entra 相同。
    /// </summary>
    public static void Validate(GoogleLoginOptions google, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(google);
        if (!google.IsConfigured)
        {
            return;
        }

        if (!AuthSetup.IsAllowedAuthority(google.Authority, environment))
        {
            throw new InvalidOperationException("Ymir:Auth:Google:Authority must be an https URL (http is allowed only for loopback in Development).");
        }

        if (string.IsNullOrWhiteSpace(google.ClientSecret))
        {
            throw new InvalidOperationException("Ymir:Auth:Google:ClientSecret is required (keep it in deploy/api/.env, ADR-0024).");
        }

        if (Normalize(google.AllowedDomains).Count == 0 && Normalize(google.AllowedEmails).Count == 0 && Normalize(google.AdminEmails).Count == 0)
        {
            throw new InvalidOperationException("Google login requires Ymir:Auth:Google:AllowedDomains or AllowedEmails; any Google account could sign in otherwise (ADR-0024).");
        }
    }

    /// <summary>
    /// id_token claims → Ymir 身分與角色；不在白名單或缺少必要 claim 時回傳 <c>null</c>。
    /// 身分鍵是 <c>sub</c>（Google 帳號不變的識別）；email 可能變更，只用於白名單比對與顯示。
    /// </summary>
    public static GoogleSignInDecision? Evaluate(ClaimsPrincipal principal, GoogleLoginOptions google)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(google);
        var subject = principal.FindFirstValue("sub");
        var email = principal.FindFirstValue("email")?.Trim();
        var verified = string.Equals(principal.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email) || !verified)
        {
            return null;
        }

        var hostedDomain = principal.FindFirstValue("hd");
        var isAdmin = Normalize(google.AdminEmails).Contains(email);
        var allowed = isAdmin
            || Normalize(google.AllowedEmails).Contains(email)
            || (!string.IsNullOrWhiteSpace(hostedDomain) && Normalize(google.AllowedDomains).Contains(hostedDomain));
        if (!allowed)
        {
            return null;
        }

        var displayName = principal.FindFirstValue("name") is { Length: > 0 } name ? name : email;
        var identity = new ExternalIdentity(Issuer, subject, Truncate(displayName, 200)!, Truncate(email, 200), Truncate(email, 320), null);
        return new GoogleSignInDecision(identity, isAdmin ? UserRole.Admin : UserRole.User);
    }

    private static async Task OnTokenValidatedAsync(TokenValidatedContext context, GoogleLoginOptions google)
    {
        var decision = context.Principal is null ? null : Evaluate(context.Principal, google);
        if (decision is null)
        {
            // 不在白名單：不建立使用者；稽核不寫 email（可能是任何人的個人帳號），只記錄有人被拒絕。
            var services = context.HttpContext.RequestServices;
            var auditLog = services.GetRequiredService<IAuditLog>();
            var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
            await auditLog.WriteAsync(new AuditEntry("anonymous", "auth.login.google", "google-account", "not-allowed", AuditResult.Denied, now, null), context.HttpContext.RequestAborted);
            context.HandleResponse();
            context.Response.Redirect("/login?error=not_allowed");
            return;
        }

        await OidcSignIn.CompleteSignInAsync(context, decision.Identity, decision.Role, "auth.login.google");
    }

    private static HashSet<string> Normalize(IEnumerable<string>? values) =>
        new((values ?? []).Select(v => v.Trim()).Where(v => v.Length > 0), StringComparer.OrdinalIgnoreCase);

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}

internal sealed record GoogleSignInDecision(ExternalIdentity Identity, UserRole Role);
