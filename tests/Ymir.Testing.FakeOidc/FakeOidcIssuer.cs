using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ymir.Testing.FakeOidc;

/// <summary>簽發 authorization code 與 id_token（RS256）。code 只能用一次、5 分鐘內有效。</summary>
public sealed class FakeOidcIssuer : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly ConcurrentDictionary<string, PendingCode> _codes = new();
    private readonly FakeOidcSettings _settings;

    public FakeOidcIssuer(FakeOidcSettings settings)
    {
        _settings = settings;
        SigningKey = new RsaSecurityKey(_rsa) { KeyId = "fake-oidc-key" };
    }

    public RsaSecurityKey SigningKey { get; }

    public string IssueCode(FakeLogin login, AuthorizeRequest request)
    {
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        _codes[code] = new PendingCode(login, request, DateTimeOffset.UtcNow.AddMinutes(5));
        return code;
    }

    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, AccessGrant> _accessTokens = new();
    private readonly ConcurrentDictionary<string, RefreshGrant> _refreshTokens = new();

    /// <summary>驗證 code、redirect_uri、client 與 PKCE；成功時回傳 id_token 與 access token（要求 offline_access 時另有 refresh token）。</summary>
    public RedeemResult? Redeem(string code, string clientId, string clientSecret, string redirectUri, string? codeVerifier, string issuer)
    {
        if (!_codes.TryRemove(code, out var pending) || pending.ExpiresAt < DateTimeOffset.UtcNow)
        {
            return null;
        }

        var request = pending.Request;
        if (clientId != _settings.ClientId || clientSecret != _settings.ClientSecret || redirectUri != request.RedirectUri)
        {
            return null;
        }

        if (request.CodeChallenge is { } challenge)
        {
            if (codeVerifier is null)
            {
                return null;
            }

            var computed = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
            if (computed != challenge)
            {
                return null;
            }
        }

        return new RedeemResult(CreateIdToken(pending.Login, request.Nonce, issuer), IssueTokens(pending.Login.Account, request.Scope));
    }

    /// <summary>refresh token 換發（每次輪替）；已撤銷或 client 不符時回傳 null（Entra 回 <c>invalid_grant</c>）。</summary>
    public FakeTokens? Refresh(string refreshToken, string clientId, string clientSecret)
    {
        if (clientId != _settings.ClientId || clientSecret != _settings.ClientSecret || !_refreshTokens.TryRemove(refreshToken, out var grant))
        {
            return null;
        }

        return IssueTokens(grant.Account, grant.Scope);
    }

    /// <summary>測試用：撤銷帳號的所有 refresh token（模擬密碼變更或權限撤回）。</summary>
    public void RevokeRefreshTokens(string account)
    {
        foreach (var (token, grant) in _refreshTokens)
        {
            if (string.Equals(grant.Account, account, StringComparison.OrdinalIgnoreCase))
            {
                _refreshTokens.TryRemove(token, out _);
            }
        }
    }

    /// <summary>Fake Graph 驗證 access token：回傳帳號（小寫），無效或過期時為 null。</summary>
    public string? ResolveAccessToken(string? accessToken) =>
        accessToken is not null && _accessTokens.TryGetValue(accessToken, out var grant) && grant.ExpiresAt > DateTimeOffset.UtcNow ? grant.Account : null;

    public string UserPrincipalName(string account) => $"{account.ToLowerInvariant()}@{_settings.UserDomain}";

    private FakeTokens IssueTokens(string account, string scope)
    {
        var normalized = account.ToLowerInvariant();
        var accessToken = "fake-at-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        _accessTokens[accessToken] = new AccessGrant(normalized, DateTimeOffset.UtcNow.Add(AccessTokenLifetime));
        string? refreshToken = null;
        if (scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("offline_access"))
        {
            refreshToken = "fake-rt-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _refreshTokens[refreshToken] = new RefreshGrant(normalized, scope);
        }

        return new FakeTokens(accessToken, refreshToken, scope);
    }

    public JsonWebKey PublicJwk()
    {
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(_rsa.ExportParameters(false)) { KeyId = SigningKey.KeyId });
        jwk.Use = "sig";
        jwk.Alg = SecurityAlgorithms.RsaSha256;
        return jwk;
    }

    public void Dispose() => _rsa.Dispose();

    /// <summary>以帳號推導固定的 GUID：同一帳號每次登入的 <c>oid</c> 相同，模擬 Entra 的不變識別。</summary>
    public static Guid StableGuid(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));

    private string CreateIdToken(FakeLogin login, string? nonce, string issuer)
    {
        var account = login.Account.ToLowerInvariant();
        var upn = $"{account}@{_settings.UserDomain}";
        var claims = new Dictionary<string, object>
        {
            ["tid"] = _settings.TenantId,
            ["oid"] = StableGuid($"oid:{account}").ToString("D"),
            // Entra 的 sub 是每個應用程式不同的 pairwise 值；Ymir 不應該用它當識別。
            ["sub"] = StableGuid($"sub:{_settings.ClientId}:{account}").ToString("N"),
            ["name"] = login.DisplayName,
            ["preferred_username"] = upn,
            ["email"] = upn,
            ["ver"] = "2.0",
        };
        if (nonce is not null)
        {
            claims["nonce"] = nonce;
        }

        if (login.IsAdmin)
        {
            claims["roles"] = new[] { _settings.AdminRole };
        }

        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = _settings.ClientId,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddHours(1),
            Claims = claims,
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
        });
    }

    private sealed record PendingCode(FakeLogin Login, AuthorizeRequest Request, DateTimeOffset ExpiresAt);

    private sealed record AccessGrant(string Account, DateTimeOffset ExpiresAt);

    private sealed record RefreshGrant(string Account, string Scope);
}

public sealed record FakeTokens(string AccessToken, string? RefreshToken, string Scope);

public sealed record RedeemResult(string IdToken, FakeTokens Tokens);

public sealed record FakeLogin(string Account, string DisplayName, bool IsAdmin);

public sealed record AuthorizeRequest(string ClientId, string RedirectUri, string State, string? Nonce, string? CodeChallenge, string Scope = "openid profile");
