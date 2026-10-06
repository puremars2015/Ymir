using System.Security.Claims;
using Microsoft.Extensions.Options;
using Ymir.Platform.Identity;
using Ymir.Platform.Users;

namespace Ymir.Api.Auth;

/// <summary>
/// Entra ID（OIDC id_token）→ <see cref="ExternalIdentity"/>（ADR-0009）。claims 保留原始名稱（<c>MapInboundClaims=false</c>）：
/// <c>iss</c> 含 tenant，<c>oid</c> 是使用者在 tenant 內不變的識別；帳號名稱（UPN）可能更名，只用於顯示。
/// </summary>
public sealed class EntraIdentityProvider(IOptions<YmirAuthOptions> options) : IIdentityProvider
{
    private readonly OidcLoginOptions _options = options.Value.Oidc;

    public ExternalIdentity? Resolve(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var subject = principal.FindFirstValue(_options.SubjectClaim);
        var issuer = principal.FindFirstValue("iss") ?? principal.FindFirst(_options.SubjectClaim)?.Issuer;
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(issuer) || issuer == ClaimsIdentity.DefaultIssuer)
        {
            return null;
        }

        var account = principal.FindFirstValue("preferred_username");
        var email = principal.FindFirstValue("email") ?? (account?.Contains('@', StringComparison.Ordinal) == true ? account : null);
        var displayName = principal.FindFirstValue("name") is { Length: > 0 } name ? name : account ?? subject;
        return new ExternalIdentity(issuer, subject, Truncate(displayName, 200)!, Truncate(account, 200), Truncate(email, 320), null);
    }

    /// <summary>角色以 Entra app role 為準（ADR-0009）。</summary>
    public UserRole ResolveRole(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.FindAll("roles").Any(c => string.Equals(c.Value, _options.AdminRole, StringComparison.Ordinal))
            ? UserRole.Admin
            : UserRole.User;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
