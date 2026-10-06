using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ymir.VibeMaker.Infrastructure.Runtime.Remote;

namespace Ymir.IntegrationTests.RuntimeHost;

/// <summary>Runtime host 的 Cloudflare Tunnel 管理端點（ADR-0010）。</summary>
public class TunnelEndpointTests(RuntimeHostFixture fixture) : IClassFixture<RuntimeHostFixture>
{
    private static readonly string ValidToken = "eyJhIjoi" + new string('A', 150) + "In0=";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SettingTheToken_WritesAPrivateEnvFile_AndRestartsCloudflared()
    {
        using var connection = fixture.CreateConnection();
        var before = fixture.Tunnel.Restarts;

        using var response = await connection.Http.PutAsJsonAsync(RuntimeHostProtocol.TunnelTokenPath, new TunnelTokenMessage(ValidToken), RuntimeHostProtocol.JsonOptions, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(before + 1, fixture.Tunnel.Restarts);
        Assert.Equal($"TUNNEL_TOKEN={ValidToken}\n", await File.ReadAllTextAsync(fixture.TunnelEnvFile, Ct));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(fixture.TunnelEnvFile));
        }

        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(fixture.TunnelEnvFile)!, "*.tmp", SearchOption.AllDirectories));
        var status = await connection.Http.GetFromJsonAsync<TunnelStatusMessage>(RuntimeHostProtocol.TunnelPath, RuntimeHostProtocol.JsonOptions, Ct);
        Assert.True(status!.ManagementEnabled);
        Assert.True(status.Configured);
        Assert.True(status.Active);
        Assert.NotNull(status.UpdatedAt);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("line\nbreak")]
    [InlineData("has space")]
    [InlineData("quote\"")]
    public async Task InvalidTokens_AreRejected_WithoutTouchingTheFile(string suffix)
    {
        using var connection = fixture.CreateConnection();
        var token = suffix == "short" ? "abc" : new string('A', 120) + suffix;
        var before = fixture.Tunnel.Restarts;
        var contentBefore = File.Exists(fixture.TunnelEnvFile) ? await File.ReadAllTextAsync(fixture.TunnelEnvFile, Ct) : null;

        using var response = await connection.Http.PutAsJsonAsync(RuntimeHostProtocol.TunnelTokenPath, new TunnelTokenMessage(token), RuntimeHostProtocol.JsonOptions, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, fixture.Tunnel.Restarts);
        Assert.Equal(contentBefore, File.Exists(fixture.TunnelEnvFile) ? await File.ReadAllTextAsync(fixture.TunnelEnvFile, Ct) : null);
    }

    [Fact]
    public async Task TrailingNewline_IsRejected()
    {
        using var connection = fixture.CreateConnection();

        using var response = await connection.Http.PutAsJsonAsync(RuntimeHostProtocol.TunnelTokenPath, new TunnelTokenMessage(ValidToken + "\n"), RuntimeHostProtocol.JsonOptions, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RestartFailure_Returns502()
    {
        using var connection = fixture.CreateConnection();
        fixture.Tunnel.FailRestart = true;
        try
        {
            using var response = await connection.Http.PutAsJsonAsync(RuntimeHostProtocol.TunnelTokenPath, new TunnelTokenMessage(ValidToken), RuntimeHostProtocol.JsonOptions, Ct);

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsStringAsync(Ct));
        }
        finally
        {
            fixture.Tunnel.FailRestart = false;
        }
    }

    [Fact]
    public async Task TunnelEndpoints_RequireTheToken()
    {
        using var connection = fixture.CreateConnection("wrong-token-wrong-token-wrong-token-0000");

        using var status = await connection.Http.GetAsync(RuntimeHostProtocol.TunnelPath, Ct);
        using var set = await connection.Http.PutAsJsonAsync(RuntimeHostProtocol.TunnelTokenPath, new TunnelTokenMessage(ValidToken), RuntimeHostProtocol.JsonOptions, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, status.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, set.StatusCode);
    }

    /// <summary>
    /// Runtime host 只能有這些端點（CLAUDE.md 安全紅線）：以 user id 操作 runtime、tunnel token、health。
    /// 新增端點必須同時更新這份清單並確認沒有接受 host 路徑、image、掛載或指令參數。
    /// </summary>
    [Fact]
    public void RuntimeHost_ExposesOnlyTheReviewedEndpoints()
    {
        var routes = fixture.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => $"{string.Join(",", e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? [])} {e.RoutePattern.RawText}")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                "DELETE /v1/users/{userId:guid}/runtime/",
                "GET /health",
                "GET /v1/edge/tunnel",
                "GET /v1/users/{userId:guid}/runtime/",
                "GET /v1/users/{userId:guid}/runtime/process",
                "POST /v1/users/{userId:guid}/runtime/",
                "POST /v1/users/{userId:guid}/runtime/start",
                "POST /v1/users/{userId:guid}/runtime/stop",
                "PUT /v1/edge/tunnel-token",
            ],
            routes);
    }
}
