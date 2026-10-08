using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ymir.McpGateway;
using Ymir.VibeMaker.Application.Agents;
using Ymir.VibeMaker.Application.Extensions;
using Ymir.VibeMaker.Application.Models;
using Ymir.VibeMaker.Application.PlatformMcp;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Domain;
using Ymir.VibeMaker.Infrastructure.PiAgent;

namespace Ymir.UnitTests.PlatformMcp;

/// <summary>ADR-0012 B：服務目錄、gateway token、平台 MCP 注入 Pi 設定的規則。</summary>
public class PlatformMcpTests
{
    private const string Key = "unit-test-signing-key-0123456789abcdef";

    private static readonly PlatformMcpServer Echo = new("echo", "測試", new Uri("http://gateway:5310/mcp/echo"));

    [Fact]
    public void Catalog_ParsesValidEntries()
    {
        var catalog = McpCatalog.Parse("""{"servers":[{"name":"eip-docs","description":" 公司文件 ","url":"https://eip.internal/mcp","credentialEnv":"EIP_TOKEN"}]}""");

        var server = Assert.Single(catalog.Servers);
        Assert.Equal("公司文件", server.Description);
        Assert.Equal("EIP_TOKEN", server.CredentialEnv);
        Assert.Same(server, catalog.Find("eip-docs"));
        Assert.Null(catalog.Find("other"));
    }

    [Theory]
    [InlineData("""{"servers":[{"name":"Bad_Name","url":"http://x"}]}""")]
    [InlineData("""{"servers":[{"name":"a","url":"file:///etc/passwd"}]}""")]
    [InlineData("""{"servers":[{"name":"a","url":"relative/path"}]}""")]
    [InlineData("""{"servers":[{"name":"a","url":"http://x"},{"name":"a","url":"http://y"}]}""")]
    [InlineData("""{"servers":[{"name":"a","url":"http://x","credentialEnv":"lower-case"}]}""")]
    [InlineData("not json")]
    public void Catalog_RejectsInvalidEntries(string json)
    {
        Assert.Throws<InvalidOperationException>(() => McpCatalog.Parse(json));
    }

    [Fact]
    public void Token_RoundTrips_AndRejectsTamperingAndExpiry()
    {
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UnixEpoch.AddYears(56);
        var token = McpGatewayToken.Issue(Key, new McpGatewayClaims(userId, ["echo", "eip"], now.AddMinutes(30)));

        var claims = McpGatewayToken.Validate(Key, token, now);
        Assert.NotNull(claims);
        Assert.Equal(userId, claims.UserId);
        Assert.Equal(["echo", "eip"], claims.Servers);

        Assert.Null(McpGatewayToken.Validate(Key, token, now.AddMinutes(31)));
        Assert.Null(McpGatewayToken.Validate("another-key-0123456789abcdef0123456789", token, now));
        var parts = token.Split('.');
        var widened = $"{parts[0]}.{Convert.ToBase64String(Encoding.UTF8.GetBytes($$"""{"sub":"{{userId}}","srv":["*"],"exp":9999999999}""")).TrimEnd('=').Replace('+', '-').Replace('/', '_')}.{parts[2]}";
        Assert.Null(McpGatewayToken.Validate(Key, widened, now));
        Assert.Null(McpGatewayToken.Validate(Key, null, now));
        Assert.Throws<InvalidOperationException>(() => McpGatewayToken.EnsureKeyIsStrong("short", "key"));
    }

    [Fact]
    public void AccessList_HonorsModeAndEnabledFlag()
    {
        var alice = Guid.NewGuid();
        var access = McpServerAccess.Create("echo", DateTimeOffset.UnixEpoch);
        Assert.False(access.Allows(alice, isAdmin: true)); // 預設停用

        access.Update(true, McpAccessMode.AdminsOnly, [], DateTimeOffset.UnixEpoch);
        Assert.True(access.Allows(alice, isAdmin: true));
        Assert.False(access.Allows(alice, isAdmin: false));

        access.Update(true, McpAccessMode.SelectedUsers, [alice], DateTimeOffset.UnixEpoch);
        Assert.True(access.Allows(alice, isAdmin: false));
        Assert.False(access.Allows(Guid.NewGuid(), isAdmin: true));
    }

    [Fact]
    public void McpConfig_PlatformEntriesOverrideUserEntries_AndReferenceTheTokenByName()
    {
        const string user = """{"mcpServers":{"echo":{"url":"http://evil.example/mcp"},"mine":{"command":"node"}}}""";

        var json = PiExtensionConfig.BuildMcpConfig(user, allowUserServers: true, out _, [Echo]);
        var servers = JsonNode.Parse(json)!["mcpServers"]!.AsObject();

        Assert.Equal("http://gateway:5310/mcp/echo", servers["echo"]!["url"]!.GetValue<string>());
        Assert.Equal("Bearer ${YMIR_MCP_TOKEN}", servers["echo"]!["headers"]!["Authorization"]!.GetValue<string>());
        Assert.NotNull(servers["mine"]);
    }

    [Fact]
    public void McpConfig_WithoutUserCapability_OnlyHasPlatformEntries()
    {
        var json = PiExtensionConfig.BuildMcpConfig("""{"mcpServers":{"mine":{"command":"node"}}}""", allowUserServers: false, out _, [Echo]);

        Assert.Equal(["echo"], JsonNode.Parse(json)!["mcpServers"]!.AsObject().Select(p => p.Key));
        Assert.Contains("--extension", PiExtensionConfig.BuildArguments(EffectiveExtensions.None, hasPlatformMcp: true));
        Assert.DoesNotContain("--extension", PiExtensionConfig.BuildArguments(EffectiveExtensions.None));
    }

    [Fact]
    public void Harness_PassesTheTokenOnlyAsEnvironmentVariable()
    {
        var harness = new PiAgentHarness(null!, Options.Create(new PiAgentOptions()), new ModelCatalog([new ModelDescriptor("m", "M")], "m"), null!, NullLogger<PiAgentHarness>.Instance);
        var request = new AgentRunRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "hi", RuntimePaths.Workspace, "sk-key", "m", [], PlatformMcp: new PlatformMcpGrant([Echo], "ymcp1.secret.token"));

        var spec = harness.BuildProcessSpec(request);

        Assert.Equal("ymcp1.secret.token", spec.Environment!["YMIR_MCP_TOKEN"]);
        Assert.DoesNotContain(spec.Arguments, a => a.Contains("ymcp1", StringComparison.Ordinal));
        Assert.DoesNotContain("ymcp1", request.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("ymcp1", request.PlatformMcp!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Gateway_AuditsOnlyToolNames()
    {
        var single = Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"echo","arguments":{"text":"secret"}}}""");
        var batch = Encoding.UTF8.GetBytes("""[{"jsonrpc":"2.0","id":1,"method":"tools/list"},{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"search"}}]""");

        Assert.Equal(["echo"], McpProxy.ToolCalls(single));
        Assert.Equal(["search"], McpProxy.ToolCalls(batch));
        Assert.Empty(McpProxy.ToolCalls(Encoding.UTF8.GetBytes("not json")));
    }
}
