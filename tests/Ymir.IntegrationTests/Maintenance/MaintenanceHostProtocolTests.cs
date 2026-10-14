using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ymir.Maintenance;

namespace Ymir.IntegrationTests.Maintenance;

/// <summary>
/// ADR-0025 §2：維護服務只有固定的查詢、政策同步、紀錄查詢與 dry run；每個請求都要 token；
/// 沒有 shell / exec / 任意刪除端點，回應不含 host 路徑。
/// </summary>
public class MaintenanceHostProtocolTests(MaintenanceHostFixture fixture) : IClassFixture<MaintenanceHostFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Raw(string? token = MaintenanceHostFixture.Token)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, ct) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(fixture.SocketPath), ct);
                return new NetworkStream(socket, ownsSocket: true);
            },
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    [Fact]
    public void ExposesOnlyTheReviewedEndpoints()
    {
        var routes = fixture.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => $"{string.Join(",", e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])} {e.RoutePattern.RawText}")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                "GET /health",
                "GET /v1/policy",
                "GET /v1/resources",
                "GET /v1/runs",
                "POST /v1/dry-run",
                "PUT /v1/policy",
            ],
            routes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-token-wrong-token-wrong-token-0000")]
    public async Task EveryEndpointRequiresTheToken(string? token)
    {
        using var client = Raw(token);
        foreach (var (method, path) in new[] { ("GET", "/v1/resources"), ("GET", "/v1/policy"), ("PUT", "/v1/policy"), ("GET", "/v1/runs"), ("POST", "/v1/dry-run") })
        {
            using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Content = new StringContent("{}", Encoding.UTF8, "application/json") }, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var health = await client.GetAsync("/health", Ct);
        Assert.Equal("ok", await health.Content.ReadAsStringAsync(Ct));
    }

    [Theory]
    [InlineData("POST", "/v1/exec")]
    [InlineData("DELETE", "/v1/images/abc")]
    [InlineData("DELETE", "/v1/containers/abc")]
    [InlineData("POST", "/v1/reclaim")]
    public async Task ArbitraryCommandsAndDeleteTargets_DoNotExist(string method, string path)
    {
        using var client = Raw();
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Ct);
        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, response.StatusCode.ToString());
    }

    [Theory]
    [InlineData("""{"enabled":true,"containerRetentionDays":-1,"imageRetentionDays":0,"cpuMinFreePercent":0,"memoryMinAvailableGb":0,"memoryMinAvailablePercent":0,"diskMinAvailableGb":0,"diskMinAvailablePercent":0}""")]
    [InlineData("""{"enabled":true,"containerRetentionDays":1,"imageRetentionDays":1,"cpuMinFreePercent":100,"memoryMinAvailableGb":0,"memoryMinAvailablePercent":0,"diskMinAvailableGb":0,"diskMinAvailablePercent":0}""")]
    [InlineData("not json")]
    public async Task InvalidPolicy_IsRejected(string body)
    {
        using var client = Raw();
        using var response = await client.PutAsync("/v1/policy", new StringContent(body, Encoding.UTF8, "application/json"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NegativeRunCursor_IsRejected()
    {
        using var client = Raw();
        using var response = await client.GetAsync("/v1/runs?after=-1", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Resources_AreSummariesWithoutHostPaths()
    {
        using var client = fixture.CreateClient();
        var snapshot = await client.GetResourcesAsync(Ct);
        using var raw = Raw();
        var json = await raw.GetStringAsync("/v1/resources", Ct);

        Assert.NotNull(snapshot);
        var container = Assert.Single(snapshot.Containers);
        Assert.True(container.IsAgentRuntime);
        // 停止且有回收 label，但 workspace 目錄在這台機器上不存在：無法確認仍可存取，fail closed。
        Assert.Equal(ProtectionReason.WorkspaceUnverified, container.Protection);
        Assert.DoesNotContain("/host/secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("/tmp", json, StringComparison.Ordinal);
        Assert.Equal(2, snapshot.Images.Count);
    }

    [Fact]
    public async Task PolicyRoundTripsThroughTheClient_AndDryRunNeverDeletes()
    {
        using var client = fixture.CreateClient();
        var policy = ReclamationPolicy.Default with { Enabled = true, ContainerRetentionDays = 3, DiskMinAvailablePercent = 10 };

        Assert.True(await client.SetPolicyAsync(policy, Ct));
        var dryRun = await client.DryRunAsync(Ct);

        Assert.NotNull(dryRun);
        Assert.Contains(dryRun.EmergencyCandidates, i => i.Kind == "image");
        lock (fixture.Cli.Calls)
        {
            Assert.DoesNotContain(fixture.Cli.Calls, c => c[0] is "rm" or "rmi");
        }

        using var wrong = fixture.CreateClient("wrong-token-wrong-token-wrong-token-0000");
        Assert.False(await wrong.SetPolicyAsync(policy, Ct));
        Assert.Null(await wrong.GetResourcesAsync(Ct));
    }
}
