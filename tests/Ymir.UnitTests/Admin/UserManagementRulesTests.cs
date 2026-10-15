using Ymir.Api.Auth;
using Ymir.Api.Endpoints;
using Ymir.Platform.Identity;
using Ymir.Platform.Users;

namespace Ymir.UnitTests.Admin;

/// <summary>使用者管理（ADR-0027）：顯示名稱覆寫、IdP 角色同步與訪客、刪除後釋放外部身分。</summary>
public class UserManagementRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    private static ExternalIdentity Identity(string issuer = "https://login.example.com/tenant/v2.0", string name = "IdP 名稱") =>
        new(issuer, "subject-1", name, "someone@example.com", "someone@example.com", null);

    [Fact]
    public void Rename_KeepsAdminNameAcrossLogins()
    {
        var user = User.Create(Identity(), UserRole.User, Now);
        user.RecordLogin(Identity(name: "第一次"), Now);
        Assert.Equal("第一次", user.DisplayName);

        user.Rename("管理員設定的名稱", Now);
        user.RecordLogin(Identity(name: "IdP 改了名字"), Now.AddDays(1));

        Assert.Equal("管理員設定的名稱", user.DisplayName);
        Assert.True(user.DisplayNameOverridden);
    }

    [Theory]
    [InlineData(UserRole.User, UserRole.Admin, UserRole.Admin)]
    [InlineData(UserRole.Guest, UserRole.Admin, UserRole.Admin)]
    [InlineData(UserRole.Admin, UserRole.User, UserRole.User)]
    [InlineData(UserRole.Guest, UserRole.User, UserRole.Guest)]
    [InlineData(UserRole.User, UserRole.User, UserRole.User)]
    public void SyncRole_IdpControlsOnlyAdmin(UserRole current, UserRole fromIdp, UserRole expected)
    {
        var user = User.Create(Identity(), current, Now);

        user.SyncRole(fromIdp, Now);

        Assert.Equal(expected, user.Role);
    }

    [Fact]
    public void Delete_ReleasesExternalIdentity()
    {
        var user = User.Create(Identity(), UserRole.User, Now);

        user.Delete(Now);

        Assert.Equal(UserStatus.Deleted, user.Status);
        Assert.Equal(User.DeletedIssuer, user.Issuer);
        Assert.Equal(user.Id.ToString("N"), user.Subject);
        Assert.Null(user.Email);
        // 歷史紀錄仍顯示原本的名稱
        Assert.Equal("IdP 名稱", user.DisplayName);
    }

    [Theory]
    [InlineData(UserRole.User, UserRole.Guest, true)]
    [InlineData(UserRole.Guest, UserRole.User, true)]
    [InlineData(UserRole.User, UserRole.Admin, false)]
    [InlineData(UserRole.Admin, UserRole.User, false)]
    [InlineData(UserRole.Admin, UserRole.Guest, false)]
    public void CanChangeRole_IdpAccounts_OnlyBetweenUserAndGuest(UserRole current, UserRole target, bool allowed)
    {
        var entra = User.Create(Identity(), current, Now);
        var google = User.Create(Identity(GoogleLogin.Issuer), current, Now);

        Assert.Equal(allowed, AdminEndpoints.CanChangeRole(entra, target));
        Assert.Equal(allowed, AdminEndpoints.CanChangeRole(google, target));
    }

    [Theory]
    [InlineData(UserRole.User, UserRole.Admin)]
    [InlineData(UserRole.Admin, UserRole.Guest)]
    [InlineData(UserRole.Guest, UserRole.Admin)]
    public void CanChangeRole_LocalAccounts_AnyRole(UserRole current, UserRole target)
    {
        var local = User.Create(LocalAccounts.IdentityFor("local.amy", "Amy", null), current, Now);

        Assert.True(AdminEndpoints.CanChangeRole(local, target));
    }
}
