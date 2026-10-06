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
        return endpoints;
    }

    private static string Root(HttpRequest request) => $"{request.Scheme}://{request.Host}";

    private static string IssuerOf(HttpRequest request, string tenant) => $"{Root(request)}/{tenant}/v2.0";

    private static readonly string[] s_code = ["code"];
    private static readonly string[] s_responseModes = ["query", "form_post"];
    private static readonly string[] s_subjectTypes = ["pairwise"];
    private static readonly string[] s_algorithms = ["RS256"];
    private static readonly string[] s_scopes = ["openid", "profile", "email"];
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
        var (authorize, error) = ParseAuthorize(tenant, settings, query["client_id"], query["redirect_uri"], query["state"], query["nonce"], query["code_challenge"], query["code_challenge_method"], query["response_type"]);
        if (authorize is null)
        {
            return Results.BadRequest(error);
        }

        if (query["login_hint"].ToString() is { Length: > 0 } hint)
        {
            var isAdmin = hint.EndsWith(AdminHintSuffix, StringComparison.Ordinal);
            var account = isAdmin ? hint[..^AdminHintSuffix.Length] : hint;
            return RedirectWithCode(issuer, authorize, new FakeLogin(account, account, isAdmin));
        }

        return Results.Content(LoginForm(authorize), "text/html; charset=utf-8");
    }

    private static async Task<IResult> AuthorizePostAsync(string tenant, HttpRequest request, FakeOidcSettings settings, FakeOidcIssuer issuer)
    {
        var form = await request.ReadFormAsync();
        var (authorize, error) = ParseAuthorize(tenant, settings, form["client_id"], form["redirect_uri"], form["state"], form["nonce"], form["code_challenge"], form["code_challenge_method"], "code");
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
        var clientId = form["client_id"].ToString();
        var clientSecret = form["client_secret"].ToString();
        // client_secret_basic
        if (request.Headers.Authorization.ToString() is { } header && header.StartsWith("Basic ", StringComparison.Ordinal))
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..])).Split(':', 2);
            clientId = WebUtility.UrlDecode(decoded[0]);
            clientSecret = decoded.Length > 1 ? WebUtility.UrlDecode(decoded[1]) : string.Empty;
        }

        if (form["grant_type"] != "authorization_code")
        {
            return Results.BadRequest(new { error = "unsupported_grant_type" });
        }

        var idToken = issuer.Redeem(form["code"].ToString(), clientId, clientSecret, form["redirect_uri"].ToString(), form["code_verifier"], IssuerOf(request, tenant));
        if (idToken is null)
        {
            return Results.BadRequest(new { error = "invalid_grant" });
        }

        return Results.Json(new Dictionary<string, object>
        {
            ["token_type"] = "Bearer",
            ["expires_in"] = 3600,
            ["access_token"] = "fake-access-token",
            ["id_token"] = idToken,
        });
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
        string? responseType)
    {
        if (tenant != settings.TenantId)
        {
            return (null, "unknown tenant");
        }

        if (clientId != settings.ClientId)
        {
            return (null, "unknown client_id");
        }

        // 模擬 Entra 的「已註冊的 redirect URI」：只接受 http(s) 且路徑為 /signin-oidc。
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var redirect) || redirect.Scheme is not ("http" or "https") || redirect.AbsolutePath != "/signin-oidc")
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

        return (new AuthorizeRequest(clientId!, redirectUri!, state!, string.IsNullOrEmpty(nonce) ? null : nonce, string.IsNullOrEmpty(codeChallenge) ? null : codeChallenge), null);
    }

    private static IResult RedirectWithCode(FakeOidcIssuer issuer, AuthorizeRequest request, FakeLogin login)
    {
        var code = issuer.IssueCode(login, request);
        return Results.Redirect(QueryHelpers.AddQueryString(request.RedirectUri, new Dictionary<string, string?> { ["code"] = code, ["state"] = request.State }));
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
              <button type="submit">登入</button>
            </form></body></html>
            """;
    }
}
