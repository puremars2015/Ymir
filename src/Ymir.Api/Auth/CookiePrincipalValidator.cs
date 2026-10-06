using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Ymir.Platform.Infrastructure.Identity;
using Ymir.Platform.Users;

namespace Ymir.Api.Auth;

/// <summary>
/// 每個帶 cookie 的請求都以 user id 查一次使用者（主鍵查詢），讓停用與角色變更立即生效（SA 驗收 #1、ADR-0009）：
/// 停用或已刪除時拒絕 cookie；角色與 cookie 不同時換發新的 cookie。
/// </summary>
internal static class CookiePrincipalValidator
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        if (!Guid.TryParse(principal?.FindFirstValue(YmirClaims.UserId), out var userId))
        {
            await RejectAsync(context);
            return;
        }

        var users = context.HttpContext.RequestServices.GetRequiredService<IUserDirectory>();
        var user = await users.FindAsync(userId, context.HttpContext.RequestAborted);
        if (user is null || user.Status == UserStatus.Disabled)
        {
            await RejectAsync(context);
            return;
        }

        if (principal!.FindFirstValue(ClaimTypes.Role) != user.Role.ToString() || principal.FindFirstValue(ClaimTypes.Name) != user.DisplayName)
        {
            context.ReplacePrincipal(YmirClaims.CreatePrincipal(user, context.Scheme.Name, YmirClaims.RequiresPasswordChange(principal)));
            context.ShouldRenew = true;
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
