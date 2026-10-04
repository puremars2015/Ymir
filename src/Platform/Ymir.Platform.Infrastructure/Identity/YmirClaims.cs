using System.Security.Claims;
using Ymir.Platform.Users;

namespace Ymir.Platform.Infrastructure.Identity;

/// <summary>登入 cookie 內的 claims。只放 Ymir 自己的使用者 id 與角色，不放 IdP token（ADR-0002）。</summary>
public static class YmirClaims
{
    public const string UserId = ClaimTypes.NameIdentifier;

    public static ClaimsPrincipal CreatePrincipal(User user, string authenticationScheme)
    {
        ArgumentNullException.ThrowIfNull(user);
        var identity = new ClaimsIdentity(
            [
                new Claim(UserId, user.Id.ToString("D")),
                new Claim(ClaimTypes.Name, user.DisplayName),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
            ],
            authenticationScheme);
        return new ClaimsPrincipal(identity);
    }
}
