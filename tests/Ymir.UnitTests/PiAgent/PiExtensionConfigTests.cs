using System.Text.Json.Nodes;
using Ymir.VibeMaker.Application.Extensions;
using Ymir.VibeMaker.Infrastructure.PiAgent;

namespace Ymir.UnitTests.PiAgent;

/// <summary>ADR-0012 A.3 / Spike 結果：Pi 參數與每次執行前重寫的設定檔。</summary>
public class PiExtensionConfigTests
{
    [Fact]
    public void NothingAllowed_DisablesExtensionsAndSkills_AndLoadsNoPlatformSkill()
    {
        var arguments = PiExtensionConfig.BuildArguments(EffectiveExtensions.None);

        Assert.Equal(["--no-extensions", "--no-skills"], arguments);
    }

    [Fact]
    public void SkillsAllowed_KeepsSkillDiscovery_ButStillDisablesExtensions()
    {
        var arguments = PiExtensionConfig.BuildArguments(new EffectiveExtensions(Skills: true, Mcp: false));

        Assert.Contains("--no-extensions", arguments);
        Assert.DoesNotContain("--no-skills", arguments);
        Assert.DoesNotContain("builtin:mcp", arguments);
        Assert.Equal(PiRuntimeLayout.ExtensionBuilderSkillDirectory, arguments[arguments.ToList().IndexOf("--skill") + 1]);
    }

    [Fact]
    public void McpAllowed_EnablesOnlyTheBuiltinMcpExtension()
    {
        var arguments = PiExtensionConfig.BuildArguments(new EffectiveExtensions(Skills: false, Mcp: true)).ToList();

        // Agent 寫的 extension 一律不載入；MCP 以內建 extension 明確開回。
        Assert.Contains("--no-extensions", arguments);
        Assert.Contains("--no-skills", arguments);
        Assert.Equal("builtin:mcp", arguments[arguments.IndexOf("--extension") + 1]);
        Assert.Single(arguments, a => a == "--extension");
        Assert.Contains("--skill", arguments);
    }

    [Fact]
    public void McpConfig_IncludesUserServers_OnlyWhenAllowed()
    {
        const string user = """{"mcpServers":{"echo":{"command":"python3","args":["echo.py"]}},"autoEnableCodemode":false}""";

        var allowed = JsonNode.Parse(PiExtensionConfig.BuildMcpConfig(user, allowUserServers: true, out var invalidAllowed))!;
        var denied = JsonNode.Parse(PiExtensionConfig.BuildMcpConfig(user, allowUserServers: false, out _))!;

        Assert.False(invalidAllowed);
        Assert.Equal("python3", allowed["mcpServers"]!["echo"]!["command"]!.GetValue<string>());
        // 只帶入 mcpServers，其他頂層設定由平台決定。
        Assert.Null(allowed["autoEnableCodemode"]);
        Assert.Empty(denied["mcpServers"]!.AsObject());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("""{"servers":{}}""")]
    [InlineData("<too large>")]
    public void McpConfig_InvalidUserFile_IsIgnoredAndReported(string user)
    {
        var config = JsonNode.Parse(PiExtensionConfig.BuildMcpConfig(user, allowUserServers: true, out var invalid))!;

        Assert.True(invalid);
        Assert.Empty(config["mcpServers"]!.AsObject());
    }

    [Fact]
    public void McpConfig_NoUserFile_IsEmptyAndValid()
    {
        var config = JsonNode.Parse(PiExtensionConfig.BuildMcpConfig(null, allowUserServers: true, out var invalid))!;

        Assert.False(invalid);
        Assert.Empty(config["mcpServers"]!.AsObject());
    }

    [Fact]
    public void Settings_NeverTrustProjects()
    {
        Assert.Equal("never", JsonNode.Parse(PiExtensionConfig.SettingsJson)!["defaultProjectTrust"]!.GetValue<string>());
        Assert.Empty(JsonNode.Parse(PiExtensionConfig.TrustJson)!.AsObject());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void BuilderSkill_DescribesOnlyWhatIsAllowed(bool skills, bool mcp)
    {
        var text = PiExtensionConfig.BuildExtensionBuilderSkill(new EffectiveExtensions(skills, mcp));

        Assert.StartsWith("---\nname: ymir-extension-builder\n", text, StringComparison.Ordinal);
        Assert.Contains(skills ? "## Skill（已開放）" : "## Skill（未開放）", text, StringComparison.Ordinal);
        Assert.Contains(mcp ? "## MCP server（已開放）" : "## MCP server（未開放）", text, StringComparison.Ordinal);
        Assert.Contains(mcp ? "/agent-state/pi-agent/mcp.user.json" : "請洽管理員", text, StringComparison.Ordinal);
    }
}
