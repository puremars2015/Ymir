using Ymir.VibeMaker.Application.Extensions;
using Ymir.VibeMaker.Domain;
using Ymir.VibeMaker.Infrastructure.PiAgent;

namespace Ymir.UnitTests.Extensions;

/// <summary>ADR-0012 A.2：全域預設 + 每人覆寫；預設拒絕。</summary>
public class ExtensionPolicyTests
{
    private static readonly Dictionary<ExtensionCapability, ExtensionGrantEffect> NoGrants = [];

    [Fact]
    public void WithoutGrants_UsesGlobalDefaults()
    {
        Assert.Equal(new EffectiveExtensions(true, false), ExtensionPolicyService.Combine(new ExtensionPolicySettings(true, false), NoGrants));
    }

    [Fact]
    public void Grants_OverrideGlobalDefaults_InBothDirections()
    {
        var grants = new Dictionary<ExtensionCapability, ExtensionGrantEffect>
        {
            [ExtensionCapability.Skills] = ExtensionGrantEffect.Deny,
            [ExtensionCapability.Mcp] = ExtensionGrantEffect.Allow,
        };

        Assert.Equal(new EffectiveExtensions(false, true), ExtensionPolicyService.Combine(new ExtensionPolicySettings(true, false), grants));
    }

    [Fact]
    public void Internet_DefaultsToAllowed_AndCanBeDeniedPerUser()
    {
        var grants = new Dictionary<ExtensionCapability, ExtensionGrantEffect> { [ExtensionCapability.Internet] = ExtensionGrantEffect.Deny };

        Assert.True(ExtensionPolicyService.Combine(new ExtensionPolicySettings(false, false), NoGrants).Internet);
        var denied = ExtensionPolicyService.Combine(new ExtensionPolicySettings(false, false), grants);
        Assert.False(denied.Internet);
        Assert.Equal(Ymir.VibeMaker.Application.Runtime.RuntimeNetworkAccess.Restricted, denied.NetworkAccess);
    }

    [Fact]
    public void SettingSavedBeforeInternetExisted_KeepsInternetAllowed()
    {
        Assert.Equal(new ExtensionPolicySettings(true, false, true), ExtensionPolicyService.Parse("{\"skills\":true,\"mcp\":false}"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    public void CorruptedSetting_IsIgnored(string json)
    {
        Assert.Null(ExtensionPolicyService.Parse(json));
    }

    [Fact]
    public void Inventory_ParsesSkillNamesAndMcpServerNames()
    {
        const string output = "skill\tdeploy\nskill\talpha\nmcp\n{\"mcpServers\":{\"echo\":{\"command\":\"x\",\"env\":{\"TOKEN\":\"secret\"}}}}";

        var inventory = PiExtensionInventory.Parse(output);

        Assert.Equal(["alpha", "deploy"], inventory.Skills);
        Assert.Equal(["echo"], inventory.McpServers);
    }

    [Theory]
    [InlineData("")]
    [InlineData("mcp\nnot json")]
    [InlineData("skill\t\nskill\tbad\u0001name\n")]
    public void Inventory_IgnoresEmptyOrInvalidEntries(string output)
    {
        var inventory = PiExtensionInventory.Parse(output);

        Assert.Empty(inventory.Skills);
        Assert.Empty(inventory.McpServers);
    }

    [Fact]
    public void Inventory_McpOnly()
    {
        var inventory = PiExtensionInventory.Parse("mcp\n{\"mcpServers\":{\"b\":{},\"a\":{}}}");

        Assert.Empty(inventory.Skills);
        Assert.Equal(["a", "b"], inventory.McpServers);
    }
}
