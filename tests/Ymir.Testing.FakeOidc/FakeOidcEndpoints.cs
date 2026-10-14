using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;

namespace Ymir.Testing.FakeOidc;

/// <summary>
/// 與 Entra v2 相同的路徑配置：<c>/{tenant}/v2.0/.well-known/openid-configuration</c>、
/// <c>/{tenant}/oauth2/v2.0/authorize</c>、<c>/{tenant}/oauth2/v2.0/token</c>、<c>/{tenant}/discovery/v2.0/keys</c>。
/// </summary>
public static class FakeOidcEndpoints
{
    /// <summary>
    /// 測試用：authorize 帶 <c>login_hint=帳號</c>（或 <c>帳號;admin</c>）時直接通過，不顯示登入表單。
    /// </summary>
    public const string AdminHintSuffix = ";admin";

    public static IEndpointRouteBuilder MapFakeOidc(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", () => Results.Text("Fake OIDC (Entra ID compatible) for Ymir development and tests."));
        endpoints.MapGet("/{tenant}/v2.0/.well-known/openid-configuration", Discovery);
        endpoints.MapGet("/{tenant}/discovery/v2.0/keys", (string tenant, FakeOidcSettings settings, FakeOidcIssuer issuer) =>
            tenant == settings.TenantId ? Results.Json(new { keys = new[] { issuer.PublicJwk() } }) : Results.NotFound());
        endpoints.MapGet("/{tenant}/oauth2/v2.0/authorize", AuthorizeGet);
        endpoints.MapPost("/{tenant}/oauth2/v2.0/authorize", AuthorizePostAsync).DisableAntiforgery();
        endpoints.MapPost("/{tenant}/oauth2/v2.0/token", TokenAsync).DisableAntiforgery();
        endpoints.MapFakeGraph();
        endpoints.MapFakeGoogle();
        return endpoints;
    }

    /// <summary>測試用：Google 的 <c>login_hint</c> 加上此後綴時，id_token 的 <c>email_verified</c> 為 false。</summary>
    public const string UnverifiedHintSuffix = ";unverified";

    /// <summary>
    /// 模擬 Google 的 OIDC（ADR-0023），issuer 為 <c>{root}/google</c>：<c>login_hint=email</c> 直接通過；
    /// 網域不是 <c>gmail.com</c> 時視為 Workspace 帳號，id_token 帶 <c>hd</c>。
    /// </summary>
    private static void MapFakeGoogle(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/google/.well-known/openid-configuration", (HttpRequest request) =>
        {
            var root = $"{Root(request)}/google";
            return Results.Json(new Dictionary<string, object>
            {
                ["issuer"] = root,
                ["authorization_endpoint"] = $"{root}/o/oauth2/v2/auth",
                ["token_endpoint"] = $"{root}/token",
                ["jwks_uri"] = $"{root}/oauth2/v3/certs",
                ["response_types_supported"] = s_code,
                ["subject_types_supported"] = s_publicSubject,
                ["id_token_signing_alg_values_supported"] = s_algorithms,
                ["scopes_supported"] = s_googleScopes,
                ["token_endpoint_auth_methods_supported"] = s_authMethods,
                ["code_challenge_methods_supported"] = s_pkceMethods,
                ["claims_supported"] = s_googleClaims,
            });
        });
        endpoints.MapGet("/google/oauth2/v3/certs", (FakeOidcIssuer issuer) => Results.Json(new { keys = new[] { issuer.PublicJwk() } }));
        endpoints.MapGet("/google/o/oauth2/v2/auth", GoogleAuthorizeGet);
        endpoints.MapPost("/google/o/oauth2/v2/auth", GoogleAuthorizePostAsync).DisableAntiforgery();
        endpoints.MapPost("/google/token", GoogleTokenAsync).DisableAntiforgery();
    }

    private static readonly string[] s_publicSubject = ["public"];
    private static readonly string[] s_googleScopes = ["openid", "email", "profile"];
    private static readonly string[] s_googleClaims = ["aud", "email", "email_verified", "exp", "hd", "iat", "iss", "name", "sub", "nonce"];

    private static IResult GoogleAuthorizeGet(HttpRequest request, FakeOidcSettings settings, FakeOidcIssuer issuer)
    {
        var query = request.Query;
        // Ymir 對 Google 省略 response_mode（授權碼預設就以 query 回傳，ADR-0023）；出現時拒絕，讓測試確認省略有生效。
        if (query.ContainsKey("response_mode"))
        {
            return Results.BadRequest("Parameter not allowed for this message type: response_mode");
        }

        var (authorize, error) = ParseAuthorize(settings.TenantId, settings, query["client_id"], query["redirect_uri"], query["state"], query["nonce"], query["code_challenge"], query["code_challenge_method"], query["response_type"], query["scope"]);
        if (authorize is null)
        {
            return Results.BadRequest(error);
        }

        if (query["login_hint"] is { Count: > 0 } hints && hints[^1] is { Length: > 0 } hint)
        {
            return RedirectWithCode(issuer, authorize, GoogleLoginFor(hint, displayName: null));
        }

        return Results.Content(GoogleLoginForm(authorize), "text/html; charset=utf-8");
    }

    private static async Task<IResult> GoogleAuthorizePostAsync(HttpRequest request, FakeOidcSettings settings, FakeOidcIssuer issuer)
    {
        var form = await request.ReadFormAsync();
        var (authorize, error) = ParseAuthorize(settings.TenantId, settings, form["client_id"], form["redirect_uri"], form["state"], form["nonce"], form["code_challenge"], form["code_challenge_method"], "code", form["scope"]);
        var email = form["email"].ToString().Trim();
        if (authorize is null || !email.Contains('@', StringComparison.Ordinal))
        {
            return Results.BadRequest(error ?? "email is required");
        }

        return RedirectWithCode(issuer, authorize, GoogleLoginFor(email, form["display_name"].ToString().Trim()));
    }

    private static FakeLogin GoogleLoginFor(string hint, string? displayName)
    {
        var unverified = hint.EndsWith(UnverifiedHintSuffix, StringComparison.Ordinal);
        var email = (unverified ? hint[..^UnverifiedHintSuffix.Length] : hint).ToLowerInvariant();
        var domain = email[(email.IndexOf('@', StringComparison.Ordinal) + 1)..];
        var account = email[..email.IndexOf('@', StringComparison.Ordinal)];
        return new FakeLogin(account, string.IsNullOrEmpty(displayName) ? account : displayName, false, new FakeGoogleAccount(email, domain == "gmail.com" ? null : domain, !unverified));
    }

    private static async Task<IResult> GoogleTokenAsync(HttpRequest request, FakeOidcIssuer issuer)
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new { error = "invalid_request" });
        }

        var form = await request.ReadFormAsync();
        var (clientId, clientSecret) = ClientCredentials(request, form);
        if (form["grant_type"] != "authorization_code")
        {
            return Results.BadRequest(new { error = "unsupported_grant_type" });
        }

        var redeemed = issuer.Redeem(form["code"].ToString(), clientId, clientSecret, form["redirect_uri"].ToString(), form["code_verifier"], $"{Root(request)}/google");
        return redeemed is null ? Results.BadRequest(new { error = "invalid_grant" }) : Results.Json(TokenResponse(redeemed.Tokens, redeemed.IdToken));
    }

    /// <summary>client_secret_post 或 client_secret_basic。</summary>
    private static (string ClientId, string ClientSecret) ClientCredentials(HttpRequest request, IFormCollection form)
    {
        if (request.Headers.Authorization.ToString() is { } header && header.StartsWith("Basic ", StringComparison.Ordinal))
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..])).Split(':', 2);
            return (WebUtility.UrlDecode(decoded[0]), decoded.Length > 1 ? WebUtility.UrlDecode(decoded[1]) : string.Empty);
        }

        return (form["client_id"].ToString(), form["client_secret"].ToString());
    }

    private static string Root(HttpRequest request) => $"{request.Scheme}://{request.Host}";

    private static string IssuerOf(HttpRequest request, string tenant) => $"{Root(request)}/{tenant}/v2.0";

    private static readonly string[] s_code = ["code"];
    private static readonly string[] s_responseModes = ["query", "form_post"];
    private static readonly string[] s_subjectTypes = ["pairwise"];
    private static readonly string[] s_algorithms = ["RS256"];
    private static readonly string[] s_scopes = ["openid", "profile", "email", "offline_access", "Files.ReadWrite", "User.Read"];

    /// <summary>模擬已註冊的 redirect URI：Entra 登入、Google 登入（ADR-0023）與 OneDrive connector（ADR-0013）。</summary>
    private static readonly string[] s_registeredRedirectPaths = ["/signin-oidc", "/signin-google", "/api/connectors/onedrive/callback"];
    private static readonly string[] s_authMethods = ["client_secret_post", "client_secret_basic"];
    private static readonly string[] s_pkceMethods = ["S256"];
    private static readonly string[] s_claims = ["sub", "iss", "aud", "exp", "iat", "nonce", "name", "preferred_username", "email", "oid", "tid", "roles"];

    private static IResult Discovery(string tenant, HttpRequest request, FakeOidcSettings settings)
    {
        if (tenant != settings.TenantId)
        {
            return Results.NotFound();
        }

        var root = Root(request);
        return Results.Json(new Dictionary<string, object>
        {
            ["issuer"] = IssuerOf(request, tenant),
            ["authorization_endpoint"] = $"{root}/{tenant}/oauth2/v2.0/authorize",
            ["token_endpoint"] = $"{root}/{tenant}/oauth2/v2.0/token",
            ["jwks_uri"] = $"{root}/{tenant}/discovery/v2.0/keys",
            ["response_types_supported"] = s_code,
            ["response_modes_supported"] = s_responseModes,
            ["subject_types_supported"] = s_subjectTypes,
            ["id_token_signing_alg_values_supported"] = s_algorithms,
            ["scopes_supported"] = s_scopes,
            ["token_endpoint_auth_methods_supported"] = s_authMethods,
            ["code_challenge_methods_supported"] = s_pkceMethods,
            ["claims_supported"] = s_claims,
        });
    }

    private static IResult AuthorizeGet(string tenant, HttpRequest request, FakeOidcSettings settings, FakeOidcIssuer issuer)
    {
        var query = request.Query;
        var (authorize, error) = ParseAuthorize(tenant, settings, query["client_id"], query["redirect_uri"], query["state"], query["nonce"], query["code_challenge"], query["code_challenge_method"], query["response_type"], query["scope"]);
        if (authorize is null)
        {
            return Results.BadRequest(error);
        }

        // 有多個 login_hint 時以最後一個為準（測試會在 Ymir 帶的 email 之後再指定帳號）；接受 帳號@網域，與 Entra 相同。
        if (query["login_hint"] is { Count: > 0 } hints && hints[^1] is { Length: > 0 } rawHint)
        {
            var hint = rawHint.EndsWith("@" + settings.UserDomain, StringComparison.OrdinalIgnoreCase) ? rawHint[..^(settings.UserDomain.Length + 1)] : rawHint;
            var isAdmin = hint.EndsWith(AdminHintSuffix, StringComparison.Ordinal);
            var account = isAdmin ? hint[..^AdminHintSuffix.Length] : hint;
            return RedirectWithCode(issuer, authorize, new FakeLogin(account, account, isAdmin));
        }

        return Results.Content(LoginForm(authorize), "text/html; charset=utf-8");
    }

    private static async Task<IResult> AuthorizePostAsync(string tenant, HttpRequest request, FakeOidcSettings settings, FakeOidcIssuer issuer)
    {
        var form = await request.ReadFormAsync();
        var (authorize, error) = ParseAuthorize(tenant, settings, form["client_id"], form["redirect_uri"], form["state"], form["nonce"], form["code_challenge"], form["code_challenge_method"], "code", form["scope"]);
        var account = form["account"].ToString().Trim();
        if (authorize is null || account.Length == 0)
        {
            return Results.BadRequest(error ?? "account is required");
        }

        var displayName = form["display_name"].ToString().Trim() is { Length: > 0 } name ? name : account;
        return RedirectWithCode(issuer, authorize, new FakeLogin(account, displayName, form["admin"] == "on"));
    }

    private static async Task<IResult> TokenAsync(string tenant, HttpRequest request, FakeOidcSettings settings, FakeOidcIssuer issuer)
    {
        if (tenant != settings.TenantId || !request.HasFormContentType)
        {
            return Results.BadRequest(new { error = "invalid_request" });
        }

        var form = await request.ReadFormAsync();
        var (clientId, clientSecret) = ClientCredentials(request, form);

        if (form["grant_type"] == "refresh_token")
        {
            // OneDrive connector（ADR-0013）：refresh token 每次換發都輪替；撤銷後回 invalid_grant（模擬密碼變更、權限撤回）。
            var refreshed = issuer.Refresh(form["refresh_token"].ToString(), clientId, clientSecret);
            return refreshed is null ? Results.BadRequest(new { error = "invalid_grant" }) : Results.Json(TokenResponse(refreshed, idToken: null));
        }

        if (form["grant_type"] != "authorization_code")
        {
            return Results.BadRequest(new { error = "unsupported_grant_type" });
        }

        var redeemed = issuer.Redeem(form["code"].ToString(), clientId, clientSecret, form["redirect_uri"].ToString(), form["code_verifier"], IssuerOf(request, tenant));
        if (redeemed is null)
        {
            return Results.BadRequest(new { error = "invalid_grant" });
        }

        return Results.Json(TokenResponse(redeemed.Tokens, redeemed.IdToken));
    }

    private static Dictionary<string, object> TokenResponse(FakeTokens tokens, string? idToken)
    {
        var response = new Dictionary<string, object>
        {
            ["token_type"] = "Bearer",
            ["expires_in"] = (int)FakeOidcIssuer.AccessTokenLifetime.TotalSeconds,
            ["access_token"] = tokens.AccessToken,
            ["scope"] = tokens.Scope,
        };
        if (tokens.RefreshToken is not null)
        {
            response["refresh_token"] = tokens.RefreshToken;
        }

        if (idToken is not null)
        {
            response["id_token"] = idToken;
        }

        return response;
    }

    private static (AuthorizeRequest? Request, string? Error) ParseAuthorize(
        string tenant,
        FakeOidcSettings settings,
        string? clientId,
        string? redirectUri,
        string? state,
        string? nonce,
        string? codeChallenge,
        string? codeChallengeMethod,
        string? responseType,
        string? scope)
    {
        if (tenant != settings.TenantId)
        {
            return (null, "unknown tenant");
        }

        if (clientId != settings.ClientId)
        {
            return (null, "unknown client_id");
        }

        // 模擬 Entra 的「已註冊的 redirect URI」：只接受 http(s) 且路徑為已註冊的 callback。
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var redirect) || redirect.Scheme is not ("http" or "https") || !s_registeredRedirectPaths.Contains(redirect.AbsolutePath))
        {
            return (null, "redirect_uri is not registered");
        }

        if (responseType != "code" || string.IsNullOrEmpty(state))
        {
            return (null, "response_type must be code and state is required");
        }

        if (!string.IsNullOrEmpty(codeChallenge) && codeChallengeMethod != "S256")
        {
            return (null, "only S256 is supported");
        }

        return (new AuthorizeRequest(clientId!, redirectUri!, state!, string.IsNullOrEmpty(nonce) ? null : nonce, string.IsNullOrEmpty(codeChallenge) ? null : codeChallenge, string.IsNullOrEmpty(scope) ? "openid profile" : scope), null);
    }

    private static IResult RedirectWithCode(FakeOidcIssuer issuer, AuthorizeRequest request, FakeLogin login)
    {
        var code = issuer.IssueCode(login, request);
        return Results.Redirect(QueryHelpers.AddQueryString(request.RedirectUri, new Dictionary<string, string?> { ["code"] = code, ["state"] = request.State }));
    }

    private static string GoogleLoginForm(AuthorizeRequest request)
    {
        static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
        return $$"""
            <!doctype html>
            <html lang="zh-Hant"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Fake Google 登入</title>
            <style>
              body { font-family: system-ui, sans-serif; background: #f1f3f4; display: grid; place-items: center; min-height: 100vh; margin: 0; }
              form { background: #fff; padding: 32px; width: min(360px, 90vw); border-radius: 8px; box-shadow: 0 1px 3px rgba(0,0,0,.3); display: grid; gap: 12px; }
              h1 { font-size: 20px; margin: 0; } .note { color: #a4262c; font-size: 13px; }
              input[type=text], input[type=email] { padding: 8px; font-size: 15px; } button { padding: 10px; background: #1a73e8; color: #fff; border: 0; border-radius: 4px; font-size: 15px; }
            </style></head>
            <body><form method="post">
              <h1>登入（Fake Google）</h1>
              <p class="note">開發 / 測試用的模擬登入頁，不會驗證密碼。gmail.com 以外的網域視為 Workspace 帳號（帶 hd）。</p>
              <label>Email <input type="email" name="email" required autofocus></label>
              <label>顯示名稱 <input type="text" name="display_name"></label>
              <input type="hidden" name="client_id" value="{{E(request.ClientId)}}">
              <input type="hidden" name="redirect_uri" value="{{E(request.RedirectUri)}}">
              <input type="hidden" name="state" value="{{E(request.State)}}">
              <input type="hidden" name="nonce" value="{{E(request.Nonce)}}">
              <input type="hidden" name="code_challenge" value="{{E(request.CodeChallenge)}}">
              <input type="hidden" name="code_challenge_method" value="S256">
              <input type="hidden" name="scope" value="{{E(request.Scope)}}">
              <button type="submit">登入</button>
            </form></body></html>
            """;
    }

    private static string LoginForm(AuthorizeRequest request)
    {
        static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
        return $$"""
            <!doctype html>
            <html lang="zh-Hant"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Fake Entra ID 登入</title>
            <style>
              body { font-family: system-ui, sans-serif; background: #f3f2f1; display: grid; place-items: center; min-height: 100vh; margin: 0; }
              form { background: #fff; padding: 32px; width: min(360px, 90vw); box-shadow: 0 2px 6px rgba(0,0,0,.2); display: grid; gap: 12px; }
              h1 { font-size: 20px; margin: 0; } .note { color: #a4262c; font-size: 13px; }
              input[type=text] { padding: 8px; font-size: 15px; } button { padding: 10px; background: #0067b8; color: #fff; border: 0; font-size: 15px; }
            </style></head>
            <body><form method="post">
              <h1>登入（Fake Entra ID）</h1>
              <p class="note">開發 / 測試用的模擬登入頁，不會驗證密碼。</p>
              <label>帳號 <input type="text" name="account" required autofocus></label>
              <label>顯示名稱 <input type="text" name="display_name"></label>
              <label><input type="checkbox" name="admin"> 具有 Ymir.Admin 角色</label>
              <input type="hidden" name="client_id" value="{{E(request.ClientId)}}">
              <input type="hidden" name="redirect_uri" value="{{E(request.RedirectUri)}}">
              <input type="hidden" name="state" value="{{E(request.State)}}">
              <input type="hidden" name="nonce" value="{{E(request.Nonce)}}">
              <input type="hidden" name="code_challenge" value="{{E(request.CodeChallenge)}}">
              <input type="hidden" name="code_challenge_method" value="S256">
              <input type="hidden" name="scope" value="{{E(request.Scope)}}">
              <button type="submit">登入</button>
            </form></body></html>
            """;
    }
}
