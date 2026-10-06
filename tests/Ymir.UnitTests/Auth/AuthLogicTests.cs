using System.Security.Claims;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Ymir.Api.Auth;
using Ymir.Platform.Users;

namespace Ymir.UnitTests.Auth;

/// <summary>ADR-0009：Entra claim 對應、導回位址、本機帳號規則、登入方式設定檢查。</summary>
public class AuthLogicTests
{
    private static EntraIdentityProvider Provider() => new(Options.Create(new YmirAuthOptions()));

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "oidc"));

    [Fact]
    public void Entra_UsesIssuerAndOid_NotPairwiseSub()
    {
        var identity = Provider().Resolve(Principal(
            ("iss", "https://login.microsoftonline.com/tenant/v2.0"),
            ("oid", "11111111-2222-3333-4444-555555555555"),
            ("sub", "pairwise-per-app"),
            ("name", "Sean Ma"),
            ("preferred_username", "sean.ma@example.com")));

        Assert.NotNull(identity);
        Assert.Equal("https://login.microsoftonline.com/tenant/v2.0", identity.Issuer);
        Assert.Equal("11111111-2222-3333-4444-555555555555", identity.Subject);
        Assert.Equal("Sean Ma", identity.DisplayName);
        Assert.Equal("sean.ma@example.com", identity.AccountName);
        Assert.Equal("sean.ma@example.com", identity.Email);
    }

    [Fact]
    public void Entra_WithoutOid_IsRejected()
    {
        Assert.Null(Provider().Resolve(Principal(("iss", "https://issuer"), ("sub", "only-sub"))));
    }

    [Fact]
    public void Entra_AdminRole_ComesFromAppRoles()
    {
        var provider = Provider();

        Assert.Equal(UserRole.Admin, provider.ResolveRole(Principal(("roles", "Other"), ("roles", "Ymir.Admin"))));
        Assert.Equal(UserRole.User, provider.ResolveRole(Principal(("roles", "ymir.admin"))));
        Assert.Equal(UserRole.User, provider.ResolveRole(Principal()));
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("/", "/")]
    [InlineData("/c/abc?x=1", "/c/abc?x=1")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("javascript:alert(1)", "/")]
    [InlineData("/ok\r\nSet-Cookie: x", "/")]
    public void SafeRedirect_OnlyAllowsLocalPaths(string? returnUrl, string expected)
    {
        Assert.Equal(expected, SafeRedirect.LocalPathOrRoot(returnUrl));
    }

    [Theory]
    [InlineData("Alice", "alice")]
    [InlineData("  bob.chen_01-x ", "bob.chen_01-x")]
    [InlineData("ab", null)]
    [InlineData("has space", null)]
    [InlineData("中文帳號", null)]
    [InlineData("a@b", null)]
    public void LocalAccount_NormalizesAccountNames(string input, string? expected)
    {
        Assert.Equal(expected, LocalAccounts.NormalizeAccount(input));
    }

    [Fact]
    public void LocalCredential_LocksAfterMaxFailures_AndSuccessResets()
    {
        var now = DateTimeOffset.Parse("2026-10-06T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var credential = LocalCredential.Create(Guid.NewGuid(), "hash", mustChangePassword: true, now);

        for (var i = 0; i < LocalAccounts.MaxFailedAttempts - 1; i++)
        {
            credential.RecordFailure(LocalAccounts.MaxFailedAttempts, LocalAccounts.LockoutDuration, now);
        }

        Assert.False(credential.IsLockedOut(now));
        credential.RecordFailure(LocalAccounts.MaxFailedAttempts, LocalAccounts.LockoutDuration, now);
        Assert.True(credential.IsLockedOut(now));
        Assert.False(credential.IsLockedOut(now + LocalAccounts.LockoutDuration + TimeSpan.FromSeconds(1)));

        credential.RecordSuccess(now);
        Assert.False(credential.IsLockedOut(now));
    }

    [Theory]
    [InlineData("short", false)]
    [InlineData("            ", false)]
    [InlineData("twelve-chars", true)]
    public void LocalAccount_PasswordPolicy(string password, bool ok)
    {
        Assert.Equal(ok, LocalAccounts.IsStrongEnough(password));
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "test";

        public string ContentRootPath { get; set; } = "/";

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Fact]
    public void Validate_ProductionNeedsAtLeastOneLoginMethod()
    {
        var none = new YmirAuthOptions { LocalAccounts = { Enabled = false } };

        Assert.Throws<InvalidOperationException>(() => AuthSetup.ValidateLoginMethods(none, new FakeEnvironment(Environments.Production)));
        AuthSetup.ValidateLoginMethods(none, new FakeEnvironment(Environments.Development));
        AuthSetup.ValidateLoginMethods(new YmirAuthOptions(), new FakeEnvironment(Environments.Production));
    }

    [Theory]
    [InlineData("http://login.example.com/tenant/v2.0", "secret", "Production")]
    [InlineData("http://login.example.com/tenant/v2.0", "secret", "Development")]
    [InlineData("https://login.microsoftonline.com/tenant/v2.0", null, "Production")]
    public void Validate_RejectsInsecureOrIncompleteOidc(string authority, string? secret, string environment)
    {
        var options = new YmirAuthOptions { Oidc = { Authority = authority, ClientId = "client", ClientSecret = secret } };

        Assert.Throws<InvalidOperationException>(() => AuthSetup.ValidateLoginMethods(options, new FakeEnvironment(environment)));
    }

    [Fact]
    public void Validate_AllowsHttpsOidc_AndLoopbackHttpInDevelopment()
    {
        AuthSetup.ValidateLoginMethods(
            new YmirAuthOptions { Oidc = { Authority = "https://login.microsoftonline.com/t/v2.0", ClientId = "c", ClientSecret = "s" } },
            new FakeEnvironment(Environments.Production));
        AuthSetup.ValidateLoginMethods(
            new YmirAuthOptions { Oidc = { Authority = "http://127.0.0.1:5299/t/v2.0", ClientId = "c", ClientSecret = "s" } },
            new FakeEnvironment(Environments.Development));
    }
}
