using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Ymir.Maintenance;
using Ymir.MaintenanceHost;

namespace Ymir.IntegrationTests.Maintenance;

/// <summary>以假的 container CLI 回應盤點：一個停止的 Agent runtime 與兩個 Ymir image（其中一個是建置留下、帶管理 label 的中間層）。</summary>
internal sealed class StaticContainerCli : IContainerCli
{
    public static readonly Guid UserId = Guid.Parse("0f0e0d0c-0b0a-0908-0706-050403020100");

    public List<IReadOnlyList<string>> Calls { get; } = [];

    public Task<CliResult> RunAsync(MaintenanceStoreOptions store, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        lock (Calls)
        {
            Calls.Add(arguments);
        }

        var output = arguments[0] switch
        {
            "info" => "/tmp",
            "ps" => "[{\"Id\":\"" + new string('c', 64) + "\",\"Names\":[\"ymir-user-" + UserId.ToString("N") + "\"],\"ImageID\":\"" + new string('a', 64)
                + "\",\"Image\":\"localhost/ymir/agent-runtime:1\",\"State\":\"exited\",\"Labels\":{\"ymir.user-id\":\"" + UserId.ToString("D") + "\",\"ymir.reclaimable\":\"agent-runtime\"}}]",
            "container" => "[{\"Id\":\"" + new string('c', 64) + "\",\"State\":{\"Status\":\"exited\"},\"Mounts\":[{\"Type\":\"bind\",\"Source\":\"/host/secret/workspaces/x\",\"Destination\":\"/workspace\"}]}]",
            "images" => "[{\"Id\":\"" + new string('a', 64) + "\",\"Names\":[\"localhost/ymir/agent-runtime:1\"],\"Size\":1000,\"Created\":1700000000,\"Labels\":{\"ymir.managed\":\"true\"}},"
                + "{\"Id\":\"" + new string('b', 64) + "\",\"Names\":null,\"Size\":500,\"Created\":1600000000,\"Labels\":{\"ymir.managed\":\"true\"}}]",
            "image" => "[]",
            _ => "[]",
        };
        return Task.FromResult(new CliResult(0, output, string.Empty));
    }
}

/// <summary>真正的維護服務（Kestrel + Unix socket），container CLI 以假的實作取代（不需要 Podman，也不需要 root）。</summary>
public sealed class MaintenanceHostFixture : IAsyncLifetime
{
    public const string Token = "integration-test-maintenance-token-0123456789";

    private WebApplication? _host;

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "ymir-mh-it-" + Guid.NewGuid().ToString("N"));

    /// <summary>Unix socket 路徑上限約 108 bytes，放在短的暫存路徑。</summary>
    public string SocketPath { get; } = Path.Combine(Path.GetTempPath(), $"ymir-mh-{Guid.NewGuid():N}.sock");

    public string Endpoint => $"unix:{SocketPath}";

    internal StaticContainerCli Cli { get; } = new();

    public IServiceProvider Services => _host!.Services;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(Root, "locks"));
        _host = MaintenanceHostApp.Build(
        [
            $"--MaintenanceHost:Listen={Endpoint}",
            $"--MaintenanceHost:Token={Token}",
            "--MaintenanceHost:SocketMode=600",
            $"--MaintenanceHost:StateDirectory={Path.Combine(Root, "state")}",
            $"--MaintenanceHost:LockDirectory={Path.Combine(Root, "locks")}",
            "--MaintenanceHost:Stores:0:Name=ymir",
            "--MaintenanceHost:Stores:0:Account=ymir",
            "--MaintenanceHost:Stores:0:ImageRepositories:0=localhost/ymir/agent-runtime",
            "--MaintenanceHost:Stores:0:AgentContainers=true",
            "--MaintenanceHost:Stores:0:AgentWorkspaceRoot=/host/secret/workspaces",
        ],
        services => services.AddSingleton<IContainerCli>(Cli));
        await _host.StartAsync(CancellationToken.None);
        // 等第一次採樣完成。
        var service = _host.Services.GetRequiredService<MaintenanceService>();
        for (var i = 0; i < 100 && service.GetSnapshot().Host.SampledAt is null; i++)
        {
            await Task.Delay(50);
        }
    }

    public MaintenanceClient CreateClient(string token = Token) =>
        new(new MaintenanceClientOptions { Endpoint = Endpoint, Token = token }, new TestOutputLogger<MaintenanceClient>());

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync(CancellationToken.None);
            await _host.DisposeAsync();
        }

        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }

        File.Delete(SocketPath);
    }
}
