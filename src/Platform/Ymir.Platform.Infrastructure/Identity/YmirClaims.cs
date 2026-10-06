using System.Security.Claims;
using Ymir.Platform.Users;

namespace Ymir.Platform.Infrastructure.Identity;

/// <summary>登入 cookie 內的 claims。只放 Ymir 自己的使用者 id 與角色，不放 IdP token（ADR-0002）。</summary>
public static class YmirClaims
{
    public const string UserId = ClaimTypes.NameIdentifier;

    /// <summary>本機帳號必須先改密碼（Admin 設定的初始 / 重設密碼，ADR-0009）；有此 claim 時只能呼叫改密碼等少數端點。</summary>
    public const string MustChangePassword = "ymir:must_change_password";

    public static ClaimsPrincipal CreatePrincipal(User user, string authenticationScheme, bool mustChangePassword = false)
    {
        ArgumentNullException.ThrowIfNull(user);
        List<Claim> claims =
        [
            new Claim(UserId, user.Id.ToString("D")),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
        ];
        if (mustChangePassword)
        {
            claims.Add(new Claim(MustChangePassword, "true"));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationScheme));
    }

    public static bool RequiresPasswordChange(ClaimsPrincipal principal) =>
        principal?.HasClaim(MustChangePassword, "true") == true;
}
