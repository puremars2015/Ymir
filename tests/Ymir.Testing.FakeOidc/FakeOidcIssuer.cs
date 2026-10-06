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

    /// <summary>驗證 code、redirect_uri、client 與 PKCE；成功時回傳 id_token。</summary>
    public string? Redeem(string code, string clientId, string clientSecret, string redirectUri, string? codeVerifier, string issuer)
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

        return CreateIdToken(pending.Login, request.Nonce, issuer);
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
}

public sealed record FakeLogin(string Account, string DisplayName, bool IsAdmin);

public sealed record AuthorizeRequest(string ClientId, string RedirectUri, string State, string? Nonce, string? CodeChallenge);
