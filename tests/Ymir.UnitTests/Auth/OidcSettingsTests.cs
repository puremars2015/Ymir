using System.Security.Claims;
using Microsoft.Extensions.Hosting;
using Ymir.Api.Auth;
using Ymir.Platform.Users;

namespace Ymir.UnitTests.Auth;

/// <summary>ADR-0010：管理介面的 Entra 設定（驗證、authority 組合、生效值）。</summary>
public class OidcSettingsTests
{
    private const string Tenant = "e333846a-0ee2-4e2d-a13b-efc97851b892";

    [Fact]
    public void Authority_IsBuiltFromTheFixedHostAndTenant()
    {
        Assert.Equal($"https://login.microsoftonline.com/{Tenant}/v2.0", OidcSettingsKeys.BuildAuthority("https://login.microsoftonline.com/", Tenant));
        Assert.Equal(Tenant, EffectiveOidcSettings.TenantFromAuthority($"https://login.microsoftonline.com/{Tenant}/v2.0"));
        Assert.Null(EffectiveOidcSettings.TenantFromAuthority("https://login.microsoftonline.com/common/v2.0"));
    }

    [Theory]
    [InlineData("not-a-guid", "client", "Ymir.Admin", "公司帳號", null)]
    [InlineData(Tenant, "client id with spaces", "Ymir.Admin", "公司帳號", null)]
    [InlineData(Tenant, "https://evil.example/", "Ymir.Admin", "公司帳號", null)]
    [InlineData(Tenant, "client", "Ymir Admin", "公司帳號", null)]
    [InlineData(Tenant, "client", "Ymir.Admin", "", null)]
    [InlineData(Tenant, "client", "Ymir.Admin", "公司帳號", "line\nbreak")]
    [InlineData(Tenant, "client", "Ymir.Admin", "公司帳號", "")]
    public void Validate_RejectsBadInput(string tenant, string client, string role, string displayName, string? secret) =>
        Assert.NotNull(OidcSettingsKeys.Validate(tenant, client, role, displayName, secret));

    [Fact]
    public void Validate_AcceptsEntraValues_AndAKeptSecret()
    {
        Assert.Null(OidcSettingsKeys.Validate(Tenant, "5e85a804-c308-4402-b621-0be6d023a2ac", "Ymir.Admin", "公司帳號", "abc~DEF.123"));
        Assert.Null(OidcSettingsKeys.Validate(Tenant, "ymir-dev", "Ymir.Admin", "公司帳號", null));
    }

    [Fact]
    public void Document_RoundTripsAsJson_AndRejectsGarbage()
    {
        var document = new OidcSettingsDocument(true, Tenant, "client", "Ymir.Admin", "公司帳號", new DateOnly(2027, 1, 31));

        Assert.Equal(document, OidcSettingsDocument.FromJson(document.ToJson()));
        Assert.DoesNotContain("clientSecret", document.ToJson(), StringComparison.OrdinalIgnoreCase);
        Assert.Null(OidcSettingsDocument.FromJson("{not json"));
    }

    [Fact]
    public void Deployment_IsConfiguredOnlyWithAuthorityClientAndSecret()
    {
        Assert.False(EffectiveOidcSettings.FromDeployment(new OidcLoginOptions()).IsConfigured);
        Assert.Equal(OidcSettingsSource.None, EffectiveOidcSettings.FromDeployment(new OidcLoginOptions()).Source);
        var configured = EffectiveOidcSettings.FromDeployment(new OidcLoginOptions
        {
            Authority = $"https://login.microsoftonline.com/{Tenant}/v2.0",
            ClientId = "client",
            ClientSecret = "secret",
        });
        Assert.True(configured.IsConfigured);
        Assert.Equal(OidcSettingsSource.Deployment, configured.SecretSource);
        Assert.Equal(Tenant, configured.TenantId);
        Assert.False((configured with { Enabled = false }).IsConfigured);
        Assert.False((configured with { ClientSecret = null }).IsConfigured);
    }

    [Theory]
    [InlineData("https://login.microsoftonline.com", "Production", true)]
    [InlineData("http://login.microsoftonline.com", "Production", false)]
    [InlineData("http://127.0.0.1:5299", "Development", true)]
    [InlineData("http://127.0.0.1:5299", "Production", false)]
    [InlineData("http://example.com", "Development", false)]
    [InlineData("not a url", "Development", false)]
    public void AuthorityHost_MustBeHttpsExceptDevelopmentLoopback(string host, string environment, bool allowed)
    {
        var options = new YmirAuthOptions { Oidc = { AuthorityHost = host } };

        if (allowed)
        {
            AuthSetup.ValidateLoginMethods(options, new AuthLogicTests.FakeEnvironment(environment));
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => AuthSetup.ValidateLoginMethods(options, new AuthLogicTests.FakeEnvironment(environment)));
        }
    }

    [Fact]
    public void EntraRoleMapping_FollowsTheCurrentSettings()
    {
        var source = new StaticOidcSettings(EffectiveOidcSettings.FromDeployment(new OidcLoginOptions()));
        var provider = new EntraIdentityProvider(source);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("roles", "Portal.Admin")], "oidc"));

        Assert.Equal(UserRole.User, provider.ResolveRole(principal));
        source.Current = source.Current with { AdminRole = "Portal.Admin" };
        Assert.Equal(UserRole.Admin, provider.ResolveRole(principal));
    }
}
