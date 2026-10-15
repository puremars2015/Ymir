using System.Net;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Ymir.VibeMaker.Application.Extensions;
using Ymir.VibeMaker.Infrastructure.PiAgent;

namespace Ymir.UnitTests.Extensions;

public class PersonalMcpTests
{
    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.100.100.200", false)]
    [InlineData("172.16.1.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("192.0.2.1", false)]
    [InlineData("198.18.0.1", false)]
    [InlineData("198.51.100.1", false)]
    [InlineData("203.0.113.1", false)]
    [InlineData("224.1.1.1", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("2002:7f00:1::", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("2001::1", false)]
    [InlineData("3fff::1", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("1.1.1.1", true)]
    [InlineData("2606:4700:4700::1111", true)]
    public void Probe_RejectsNonpublicAndTransitionAddresses(string address, bool expected) =>
        Assert.Equal(expected, PersonalMcpProbe.IsPublicAddress(IPAddress.Parse(address)));

    [Theory]
    [InlineData("https://tools.example.com/mcp", true)]
    [InlineData("https://tools.example.com:443/mcp", true)]
    [InlineData("http://tools.example.com/mcp", false)]
    [InlineData("https://user:pass@tools.example.com/mcp", false)]
    [InlineData("https://tools.example.com/mcp?token=secret", false)]
    [InlineData("https://tools.example.com/mcp#secret", false)]
    [InlineData("https://tools.example.com:8443/mcp", false)]
    [InlineData("https://127.0.0.1/mcp", false)]
    [InlineData("https://[::1]/mcp", false)]
    [InlineData("https://localhost/mcp", false)]
    [InlineData("https://anything.local/mcp", false)]
    [InlineData("file:///etc/passwd", false)]
    public void Registration_RejectsUnsafeUrlsAndUrlCredentials(string url, bool expected) =>
        Assert.Equal(expected, PersonalMcpService.IsValidUrl(url));

    [Fact]
    public void Probe_DisablesRedirectProxyAndCookies()
    {
        using var handler = PersonalMcpProbe.Handler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseProxy);
        Assert.False(handler.UseCookies);
        Assert.NotNull(handler.ConnectCallback);
    }

    [Fact]
    public void Secret_IsEncryptedAndBoundToItsOwner()
    {
        var secrets = new PersonalMcpSecrets(new EphemeralDataProtectionProvider());
        var user = Guid.NewGuid();
        var encrypted = secrets.Protect(user, "private-token");
        Assert.DoesNotContain("private-token", encrypted, StringComparison.Ordinal);
        Assert.Equal("private-token", secrets.Unprotect(user, encrypted));
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() => secrets.Unprotect(Guid.NewGuid(), encrypted));
    }

    [Fact]
    public void Config_ReferencesEnvironmentAndNeverWritesPersonalToken()
    {
        var personal = new PersonalMcpGrant(Guid.NewGuid(), "https://tools.example.com/mcp", "private-token");
        var json = PiExtensionConfig.BuildMcpConfig(null, true, out _, personalServers: [personal]);
        Assert.Contains(personal.ServerName, json, StringComparison.Ordinal);
        Assert.Contains(personal.TokenVariable, json, StringComparison.Ordinal);
        Assert.DoesNotContain("private-token", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private-token", personal.ToString(), StringComparison.Ordinal);
        var denied = PiExtensionConfig.BuildMcpConfig(null, false, out _, personalServers: [personal]);
        Assert.DoesNotContain(personal.ServerName, denied, StringComparison.Ordinal);
    }

    private const string Initialized = """{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-03-26","capabilities":{},"serverInfo":{"name":"fixture","version":"1"}}}""";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Probe_SendsOnlyInitializeAndAcceptsJsonOrSse(bool sse)
    {
        using var handler = new ReplyHandler(sse ? "event: message\ndata: " + Initialized + "\n\n" : Initialized, sse);
        using var http = new HttpClient(handler);
        Assert.True(await new PersonalMcpProbe(http).TestAsync(new Uri("https://tools.example.com/mcp"), "fixture-token", TestContext.Current.CancellationToken));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("{\"jsonrpc\":2}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"message\":\"secret error\"}}")]
    [InlineData("[]")]
    [InlineData("not json")]
    public async Task Probe_RejectsInvalidOrErrorResponses(string body)
    {
        using var handler = new ReplyHandler(body, false);
        using var http = new HttpClient(handler);
        Assert.False(await new PersonalMcpProbe(http).TestAsync(new Uri("https://tools.example.com/mcp"), "fixture-token", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Probe_RejectsOversizeResponse()
    {
        using var handler = new ReplyHandler(new string(' ', 65537) + Initialized, false);
        using var http = new HttpClient(handler);
        Assert.False(await new PersonalMcpProbe(http).TestAsync(new Uri("https://tools.example.com/mcp"), "fixture-token", TestContext.Current.CancellationToken));
    }

    private sealed class ReplyHandler(string body, bool sse) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("Bearer fixture-token", request.Headers.Authorization?.ToString());
            var content = await request.Content!.ReadAsStringAsync(cancellationToken);
            Assert.Contains("initialize", content, StringComparison.Ordinal);
            Assert.DoesNotContain("tools/call", content, StringComparison.Ordinal);
            Assert.DoesNotContain("fixture-token", content, StringComparison.Ordinal);
            return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, sse ? "text/event-stream" : "application/json") };
        }
    }
}
