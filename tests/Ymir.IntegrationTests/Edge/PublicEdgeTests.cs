using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Ymir.Edge;

namespace Ymir.IntegrationTests.Edge;

/// <summary>ADR-0006：經由 Cloudflare Tunnel 對外時的 API 端防護（不需要資料庫，用最小 host 驗證 middleware 行為）。</summary>
public sealed class PublicEdgeTests
{
    private const string PublicHost = "ymir.example.com";

    /// <summary>測試用 header：模擬 TCP 連線的來源 IP（TestServer 預設沒有來源 IP）。</summary>
    private const string TestRemoteIpHeader = "X-Test-Remote-Ip";

    private static async Task<WebApplication> StartAsync(string environment, params (string Key, string Value)[] settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings.Select(s => KeyValuePair.Create(s.Key, (string?)s.Value)));
        builder.Services.AddPublicEdge(builder.Configuration, builder.Environment);

        var app = builder.Build();
        app.Use((context, next) =>
        {
            if (IPAddress.TryParse(context.Request.Headers[TestRemoteIpHeader], out var remote))
            {
                context.Connection.RemoteIpAddress = remote;
            }

            return next(context);
        });
        app.UsePublicEdge();
        app.MapGet("/echo", (HttpContext context) => $"{context.Request.Scheme} {context.Connection.RemoteIpAddress}");
        app.MapGet("/health", () => "Healthy");
        app.MapGet("/alive", () => "Healthy");
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static Task<WebApplication> StartPublicAsync(params (string Key, string Value)[] extra) =>
        StartAsync(Environments.Production, [("Ymir:PublicEdge:Enabled", "true"), ("Ymir:PublicEdge:PublicHostname", PublicHost), .. extra]);

    private static HttpRequestMessage Echo(string host, string? remoteIp = "127.0.0.1", string? forwardedFor = null, string? forwardedProto = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://{host}/echo");
        if (remoteIp is not null)
        {
            request.Headers.Add(TestRemoteIpHeader, remoteIp);
        }

        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        if (forwardedProto is not null)
        {
            request.Headers.Add("X-Forwarded-Proto", forwardedProto);
        }

        return request;
    }

    [Fact]
    public async Task ForwardedHeaders_FromLoopbackCloudflared_AreTrusted()
    {
        await using var app = await StartPublicAsync();
        using var client = app.GetTestClient();

        using var response = await client.SendAsync(Echo(PublicHost, forwardedFor: "203.0.113.7", forwardedProto: "https"), TestContext.Current.CancellationToken);

        Assert.Equal("https 203.0.113.7", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task ForwardedHeaders_FromUnknownSource_AreIgnored()
    {
        await using var app = await StartPublicAsync();
        using var client = app.GetTestClient();

        using var response = await client.SendAsync(Echo(PublicHost, remoteIp: "198.51.100.9", forwardedFor: "203.0.113.7", forwardedProto: "https"), TestContext.Current.CancellationToken);

        Assert.Equal("http 198.51.100.9", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ForwardedFor_UsesOnlyTheValueAddedByCloudflared()
    {
        await using var app = await StartPublicAsync();
        using var client = app.GetTestClient();

        // 用戶端自己塞的 X-Forwarded-For 會排在前面；只採用最後一個（cloudflared 加上的）。
        using var response = await client.SendAsync(Echo(PublicHost, forwardedFor: "10.0.0.1, 203.0.113.7", forwardedProto: "https"), TestContext.Current.CancellationToken);

        Assert.Equal("https 203.0.113.7", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConfiguredKnownProxy_ReplacesLoopbackDefault()
    {
        await using var app = await StartPublicAsync(("Ymir:PublicEdge:KnownProxies:0", "172.17.0.2"));
        using var client = app.GetTestClient();

        using var fromContainer = await client.SendAsync(Echo(PublicHost, remoteIp: "172.17.0.2", forwardedFor: "203.0.113.7", forwardedProto: "https"), TestContext.Current.CancellationToken);
        using var fromLoopback = await client.SendAsync(Echo(PublicHost, forwardedFor: "203.0.113.7", forwardedProto: "https"), TestContext.Current.CancellationToken);

        Assert.Equal("https 203.0.113.7", await fromContainer.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("http 127.0.0.1", await fromLoopback.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(PublicHost, HttpStatusCode.OK)]
    [InlineData("localhost", HttpStatusCode.OK)]
    [InlineData("attacker.example.net", HttpStatusCode.BadRequest)]
    [InlineData("random.trycloudflare.com", HttpStatusCode.BadRequest)]
    public async Task HostHeader_IsRestrictedToPublicHostnameAndLocalhost(string host, HttpStatusCode expected)
    {
        await using var app = await StartPublicAsync();
        using var client = app.GetTestClient();

        using var response = await client.SendAsync(Echo(host), TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(PublicHost, "/health", HttpStatusCode.NotFound)]
    [InlineData(PublicHost, "/ALIVE", HttpStatusCode.NotFound)]
    [InlineData(PublicHost, "/health/ready", HttpStatusCode.NotFound)]
    [InlineData("localhost", "/health", HttpStatusCode.OK)]
    [InlineData("localhost", "/alive", HttpStatusCode.OK)]
    public async Task HealthChecks_AreHiddenFromPublicHostname(string host, string path, HttpStatusCode expected)
    {
        await using var app = await StartPublicAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri($"http://{host}{path}"), TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Disabled_LeavesHostUnchanged()
    {
        await using var app = await StartAsync(Environments.Production);
        using var client = app.GetTestClient();

        using var response = await client.SendAsync(Echo("anything.example.net", forwardedFor: "203.0.113.7", forwardedProto: "https"), TestContext.Current.CancellationToken);

        Assert.Equal("http 127.0.0.1", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Development_RefusesToStartPublicly()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StartAsync(Environments.Development, ("Ymir:PublicEdge:Enabled", "true"), ("Ymir:PublicEdge:PublicHostname", PublicHost)));
        Assert.Contains("Development", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://ymir.example.com")]
    [InlineData("ymir.example.com:443")]
    [InlineData("10.0.0.5")]
    public async Task InvalidPublicHostname_RefusesToStart(string hostname)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StartAsync(Environments.Production, ("Ymir:PublicEdge:Enabled", "true"), ("Ymir:PublicEdge:PublicHostname", hostname)));
    }

    [Fact]
    public async Task InvalidKnownProxy_RefusesToStart()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => StartPublicAsync(("Ymir:PublicEdge:KnownProxies:0", "cloudflared")));
    }
}
