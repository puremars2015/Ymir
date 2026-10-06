using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.Executions;
using Ymir.IntegrationTests.PiAgent;
using Ymir.Testing.FakeLlm;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Contracts.Executions;
using Ymir.VibeMaker.Infrastructure.Runtime;

namespace Ymir.IntegrationTests.RuntimeHost;

/// <summary>正式 API 流程使用 <c>VibeMaker:Runtime:Provider=Remote</c>（API 在容器內的設定，ADR-0008）+ runtime host + 真實 Pi。</summary>
public sealed class RemoteRuntimeApiFactory : ApiFactory
{
    private readonly RuntimeHostFixture _runtimeHost = new();

    public string HostWorkspaceRoot => _runtimeHost.WorkspaceRoot;

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
        Assert.Equal(FakeLlmScript.CreatedFileContent, await File.ReadAllTextAsync(Path.Combine(directory, FakeLlmScript.CreatedFileName), ct));
        Assert.False(Directory.Exists(Path.Combine(factory.WorkspaceRoot, "must-not-be-used")));

        // 檔案下載也經由 runtime host（API 容器不掛載 workspace，ADR-0008）
        var files = await client.GetFromJsonAsync<Ymir.VibeMaker.Contracts.Files.WorkspaceFilesResponse>($"/api/conversations/{conversation.Id}/files", JsonDefaults.Options, ct);
        Assert.Equal([FakeLlmScript.CreatedFileName], files!.Files.Select(f => f.Path));
        Assert.Equal(
            FakeLlmScript.CreatedFileContent,
            await client.GetStringAsync($"/api/conversations/{conversation.Id}/files/download?path={FakeLlmScript.CreatedFileName}", ct));
        using var archive = await client.GetAsync($"/api/conversations/{conversation.Id}/files/archive", ct);
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(await archive.Content.ReadAsByteArrayAsync(ct)));
        Assert.Equal([FakeLlmScript.CreatedFileName], zip.Entries.Select(e => e.FullName));
    }
}
