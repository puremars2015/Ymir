using System.Security.Claims;
using Microsoft.Extensions.Hosting;
using Ymir.Api.Auth;
using Ymir.Platform.Users;

namespace Ymir.UnitTests.Auth;

/// <summary>ADR-0024：Google 帳號的白名單、身分鍵與部署設定檢查。</summary>
public class GoogleLoginTests
{
    private static GoogleLoginOptions Options() => new()
    {
        ClientId = "client",
        ClientSecret = "secret",
        AllowedDomains = ["acme.test"],
        AllowedEmails = [" Partner@gmail.com "],
        AdminEmails = ["boss@acme.test"],
    };

    private static ClaimsPrincipal Google(string email, string? hd = null, string verified = "true", string sub = "109876543210987654321", string? name = "Alice")
    {
        var claims = new List<Claim> { new("iss", "accounts.google.com"), new("sub", sub), new("email", email), new("email_verified", verified) };
        if (hd is not null)
        {
            claims.Add(new Claim("hd", hd));
        }

        if (name is not null)
        {
            claims.Add(new Claim("name", name));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "google"));
    }

    [Fact]
    public void WorkspaceAccount_InAllowedDomain_UsesCanonicalIssuerAndSub()
    {
        var decision = GoogleLogin.Evaluate(Google("alice@acme.test", hd: "ACME.test"), Options());

        Assert.NotNull(decision);
        Assert.Equal(GoogleLogin.Issuer, decision.Identity.Issuer);
        Assert.Equal("109876543210987654321", decision.Identity.Subject);
        Assert.Equal("Alice", decision.Identity.DisplayName);
        Assert.Equal("alice@acme.test", decision.Identity.Email);
        Assert.Equal(UserRole.User, decision.Role);
    }

    [Fact]
    public void EmailSuffix_WithoutHostedDomain_IsNotEnough()
    {
        // 個人帳號也可以用任何網域的 email 註冊 Google；只信任 Workspace 的 hd claim。
        Assert.Null(GoogleLogin.Evaluate(Google("alice@acme.test"), Options()));
    }

    [Fact]
    public void AllowedEmail_IsCaseInsensitive_AndAdminEmailIsAdmin()
    {
        Assert.Equal(UserRole.User, GoogleLogin.Evaluate(Google("partner@GMAIL.com"), Options())!.Role);
        Assert.Equal(UserRole.Admin, GoogleLogin.Evaluate(Google("Boss@acme.test", hd: "acme.test"), Options())!.Role);
    }

    [Theory]
    [InlineData("stranger@gmail.com", null, "true")]
    [InlineData("eve@evil.test", "evil.test", "true")]
    [InlineData("partner@gmail.com", null, "false")]
    [InlineData("alice@acme.test", "acme.test", "false")]
    public void NotAllowedOrUnverified_IsRejected(string email, string? hd, string verified)
    {
        Assert.Null(GoogleLogin.Evaluate(Google(email, hd, verified), Options()));
    }

    [Fact]
    public void MissingSub_IsRejected_AndDisplayNameFallsBackToEmail()
    {
        Assert.Null(GoogleLogin.Evaluate(Google("alice@acme.test", hd: "acme.test", sub: ""), Options()));
        Assert.Equal("alice@acme.test", GoogleLogin.Evaluate(Google("alice@acme.test", hd: "acme.test", name: null), Options())!.Identity.DisplayName);
    }

    [Fact]
    public void Validate_RequiresSecretAndAllowList()
    {
        var production = new AuthLogicTests.FakeEnvironment(Environments.Production);
        GoogleLogin.Validate(Options(), production);
        GoogleLogin.Validate(new GoogleLoginOptions(), production);

        Assert.Throws<InvalidOperationException>(() => GoogleLogin.Validate(new GoogleLoginOptions { ClientId = "c", ClientSecret = "s" }, production));
        Assert.Throws<InvalidOperationException>(() => GoogleLogin.Validate(new GoogleLoginOptions { ClientId = "c", AllowedDomains = ["acme.test"] }, production));
        Assert.Throws<InvalidOperationException>(() => GoogleLogin.Validate(new GoogleLoginOptions { ClientId = "c", ClientSecret = "s", AllowedDomains = [" "] }, production));
        Assert.Throws<InvalidOperationException>(() => GoogleLogin.Validate(new GoogleLoginOptions { Authority = "http://127.0.0.1:5299/google", ClientId = "c", ClientSecret = "s", AllowedDomains = ["acme.test"] }, production));
        GoogleLogin.Validate(new GoogleLoginOptions { Authority = "http://127.0.0.1:5299/google", ClientId = "c", ClientSecret = "s", AllowedDomains = ["acme.test"] }, new AuthLogicTests.FakeEnvironment(Environments.Development));
    }

    [Fact]
    public void GoogleAlone_IsAValidLoginMethod()
    {
        var options = new YmirAuthOptions { LocalAccounts = { Enabled = false }, Google = Options() };

        AuthSetup.ValidateLoginMethods(options, new AuthLogicTests.FakeEnvironment(Environments.Production));
    }
}
