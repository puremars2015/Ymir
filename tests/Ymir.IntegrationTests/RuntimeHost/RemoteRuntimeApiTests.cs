using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.Executions;
using Ymir.IntegrationTests.PiAgent;
using Ymir.Testing.FakeLlm;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Application.Files;
using Ymir.VibeMaker.Contracts.Files;
using Ymir.VibeMaker.Contracts.Executions;
using Ymir.VibeMaker.Infrastructure.Runtime;

namespace Ymir.IntegrationTests.RuntimeHost;

/// <summary>正式 API 流程使用 <c>VibeMaker:Runtime:Provider=Remote</c>（API 在容器內的設定，ADR-0008）+ runtime host + 真實 Pi。</summary>
public sealed class RemoteRuntimeApiFactory : ApiFactory
{
    private readonly RuntimeHostFixture _runtimeHost = new();

    public string HostWorkspaceRoot => _runtimeHost.WorkspaceRoot;

    public FakeTunnelServiceController Tunnel => _runtimeHost.Tunnel;

    public string TunnelEnvFile => _runtimeHost.TunnelEnvFile;

    protected override void Configure(IWebHostBuilder builder)
    {
        _runtimeHost.InitializeAsync().AsTask().GetAwaiter().GetResult();
        builder.UseSetting("VibeMaker:Harness", "Pi");
        builder.UseSetting("VibeMaker:Pi:ModelBaseUrl", _runtimeHost.FakeLlm.BaseUrl.ToString());
        builder.UseSetting("VibeMaker:Pi:ModelId", FakeLlmEndpoints.ModelId);
        builder.UseSetting("VibeMaker:Pi:DevelopmentApiKey", PiHarnessFixture.ModelApiKey);
        builder.UseSetting("VibeMaker:Pi:AutoRetry", "false");
        builder.UseSetting("VibeMaker:Runtime:Provider", "Remote");
        builder.UseSetting("VibeMaker:Runtime:Remote:Endpoint", _runtimeHost.Endpoint);
        builder.UseSetting("VibeMaker:Runtime:Remote:Token", RuntimeHostFixture.Token);
        // API 端的 WorkspaceRoot 不應被使用：指向不存在的位置，確保檔案只出現在 runtime host 的目錄。
        builder.UseSetting("VibeMaker:Runtime:WorkspaceRoot", Path.Combine(WorkspaceRoot, "must-not-be-used"));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _runtimeHost.DisposeAsync();
    }
}

public class RemoteRuntimeApiTests(RemoteRuntimeApiFactory factory) : IClassFixture<RemoteRuntimeApiFactory>
{
    /// <summary>ADR-0010：管理介面的 tunnel token 經 API 轉給 runtime host；API 回應與稽核都不含 token。</summary>
    [Fact]
    public async Task TunnelToken_IsForwardedToTheRuntimeHost_AndNeverEchoed()
    {
        var ct = TestContext.Current.CancellationToken;
        var token = "eyJhIjoi" + new string('Q', 150) + "fQ==";
        using var admin = await factory.LoginAsync($"tunnel-admin-{Guid.NewGuid():N}", Ymir.Platform.Users.UserRole.Admin);

        var before = await admin.GetFromJsonAsync<TunnelSettingsResponse>("/api/admin/settings/tunnel", JsonDefaults.Options, ct);
        var restarts = factory.Tunnel.Restarts;
        using var set = await admin.PutAsJsonAsync("/api/admin/settings/tunnel/token", new SetTunnelTokenRequest(token), ct);
        var body = await set.Content.ReadAsStringAsync(ct);

        Assert.True(before!.ManagementAvailable);
        Assert.Equal(System.Net.HttpStatusCode.OK, set.StatusCode);
        Assert.DoesNotContain(token, body, StringComparison.Ordinal);
        var after = System.Text.Json.JsonSerializer.Deserialize<TunnelSettingsResponse>(body, JsonDefaults.Options)!;
        Assert.True(after.Configured);
        Assert.True(after.Active);
        Assert.NotNull(after.TokenUpdatedAt);
        Assert.Equal(restarts + 1, factory.Tunnel.Restarts);
        Assert.Equal($"TUNNEL_TOKEN={token}\n", await File.ReadAllTextAsync(factory.TunnelEnvFile, ct));

        var audit = await admin.GetStringAsync("/api/admin/audit?action=admin.settings.tunnel", ct);
        Assert.Contains("admin.settings.tunnel.token", audit, StringComparison.Ordinal);
        Assert.DoesNotContain(token, audit, StringComparison.Ordinal);

        using var invalid = await admin.PutAsJsonAsync("/api/admin/settings/tunnel/token", new SetTunnelTokenRequest("not a token"), ct);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task SendMessage_RunsPiThroughRuntimeHost()
    {
        Assert.SkipUnless(PiHarnessFixture.IsPiOnPath(), "pi is not on PATH (npm i -g @earendil-works/pi-coding-agent@1.0.0)");
        var ct = TestContext.Current.CancellationToken;
        using var client = await factory.LoginAsync("remote-user");
        var project = await client.CreateProjectAsync("remote");
        var conversation = await client.CreateConversationAsync(project.Id, "remote chat");

        var (_, sent) = await client.SendMessageAsync(conversation.Id, $"{FakeLlmScript.CreateFileMarker} 建立檔案");
        var events = await client.ReadEventsAsync(sent!.EventStreamUrl);

        Assert.Equal(ExecutionEventNames.ExecutionCompleted, events[^1].EventType);
        var me = await client.GetFromJsonAsync<MeResponse>("/api/me", JsonDefaults.Options, ct);
        var directory = UserDirectories.For(factory.HostWorkspaceRoot, me!.Id).HostPathOf(RuntimePaths.ProjectDirectory(project.Id));
        Assert.Equal(FakeLlmScript.CreatedFileContent, await File.ReadAllTextAsync(Path.Combine(directory, ArtifactService.DirectoryFor(sent.ExecutionId), FakeLlmScript.CreatedFileName), ct));
        Assert.False(Directory.Exists(Path.Combine(factory.WorkspaceRoot, "must-not-be-used")));

        // 檔案下載也經由 runtime host（API 容器不掛載 workspace，ADR-0008）
        var groups = await client.GetFromJsonAsync<List<ArtifactGroupResponse>>($"/api/conversations/{conversation.Id}/artifacts", JsonDefaults.Options, ct);
        var files = Assert.Single(groups!);
        Assert.Equal([FakeLlmScript.CreatedFileName], files!.Files.Select(f => f.Path));
        Assert.Equal(
            FakeLlmScript.CreatedFileContent,
            await client.GetStringAsync($"/api/conversations/{conversation.Id}/artifacts/{sent.ExecutionId}/download?path={FakeLlmScript.CreatedFileName}", ct));
        using var archive = await client.GetAsync($"/api/conversations/{conversation.Id}/artifacts/{sent.ExecutionId}/archive", ct);
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(await archive.Content.ReadAsByteArrayAsync(ct)));
        Assert.Equal([FakeLlmScript.CreatedFileName], zip.Entries.Select(e => e.FullName));
    }
}
