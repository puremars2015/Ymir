using System.Security.Claims;

namespace Ymir.Platform.Identity;

/// <summary>
/// 把 ASP.NET Core 驗證後的 principal 轉成 <see cref="ExternalIdentity"/>。
/// 每種 IdP（Entra ID / OIDC、AD/LDAP adapter、Dev 測試登入）各有一個實作，業務層不直接依賴 IdP。
/// </summary>
public interface IIdentityProvider
{
    /// <summary>無法辨識（缺少 issuer/subject）時回傳 <c>null</c>。</summary>
    ExternalIdentity? Resolve(ClaimsPrincipal principal);
}
