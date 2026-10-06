using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ymir.IntegrationTests.PiAgent;
using Ymir.RuntimeHost;
using Ymir.Testing.FakeLlm;
using Ymir.VibeMaker.Application.Agents;
using Ymir.VibeMaker.Infrastructure.PiAgent;
using Ymir.VibeMaker.Infrastructure.Runtime;
using Ymir.VibeMaker.Infrastructure.Runtime.Remote;

namespace Ymir.IntegrationTests.RuntimeHost;

/// <summary>
/// 真正的 runtime host（Kestrel + Unix socket，ADR-0008）+ RemoteRuntimeManager。
/// Runtime host 內使用 Local runtime（Development），所以不需要 Podman 也能驗證整條 stdio 轉送。
/// </summary>
public sealed class RuntimeHostFixture : IAsyncLifetime
{
    public const string Token = "integration-test-runtime-host-token-0123456789";

    private WebApplication? _host;

    public string WorkspaceRoot { get; } = Path.Combine(Path.GetTempPath(), "ymir-rh-it-" + Guid.NewGuid().ToString("N"));

    /// <summary>Unix socket 路徑上限約 108 bytes，放在短的暫存路徑。</summary>
    public string SocketPath { get; } = Path.Combine(Path.GetTempPath(), $"ymir-rh-{Guid.NewGuid():N}.sock");

    public string Endpoint => $"unix:{SocketPath}";

    public FakeLlmServer FakeLlm { get; private set; } = null!;

    /// <summary>Tunnel 管理以 SystemdUser 模式開啟，但重啟服務改由 fake 記錄（沙箱沒有 systemd user session）。</summary>
    public FakeTunnelServiceController Tunnel { get; } = new();

    public string TunnelEnvFile => Path.Combine(WorkspaceRoot, "tunnel", "cloudflared.env");

    public IServiceProvider Services => _host!.Services;

    internal RemoteRuntimeManager RuntimeManager { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        FakeLlm = await FakeLlmServer.StartAsync();
        _host = RuntimeHostApp.Build(
        [
            "--environment=Development",
            $"--RuntimeHost:Listen={Endpoint}",
            $"--RuntimeHost:Token={Token}",
            "--RuntimeHost:SocketMode=600",
            "--VibeMaker:Runtime:Provider=Local",
            $"--VibeMaker:Runtime:WorkspaceRoot={WorkspaceRoot}",
            "--RuntimeHost:Tunnel:Mode=SystemdUser",
            $"--RuntimeHost:Tunnel:EnvFile={TunnelEnvFile}",
        ],
        services => services.AddSingleton<ITunnelServiceController>(Tunnel));
        await _host.StartAsync(CancellationToken.None);
        RuntimeManager = CreateManager(Token);
    }

    internal RemoteRuntimeManager CreateManager(string token) =>
        new(
            Options.Create(new RuntimeOptions { Provider = RuntimeProvider.Remote, Remote = new RemoteRuntimeOptions { Endpoint = Endpoint, Token = token } }),
            new TestOutputLogger<RemoteRuntimeManager>());

    internal RuntimeHostConnection CreateConnection(string token = Token) => new(RuntimeHostEndpoint.Parse(Endpoint, "test"), token);

    public IAgentHarness CreateHarness() =>
        new PiAgentHarness(
            RuntimeManager,
            Options.Create(new PiAgentOptions
            {
                ModelBaseUrl = FakeLlm.BaseUrl,
                ModelId = FakeLlmEndpoints.ModelId,
                DevelopmentApiKey = PiHarnessFixture.ModelApiKey,
                AutoRetry = false,
                AbortGracePeriod = TimeSpan.FromSeconds(5),
            }),
            PiHarnessFixture.Catalog,
            new TestOutputLogger<PiAgentHarness>());

    public async ValueTask DisposeAsync()
    {
        RuntimeManager?.Dispose();
        if (_host is not null)
        {
            await _host.StopAsync(CancellationToken.None);
            await _host.DisposeAsync();
        }

        await FakeLlm.DisposeAsync();
        if (Directory.Exists(WorkspaceRoot))
        {
            Directory.Delete(WorkspaceRoot, recursive: true);
        }

        File.Delete(SocketPath);
    }
}

/// <summary>測試用：記錄重啟次數，可模擬重啟失敗。</summary>
public sealed class FakeTunnelServiceController : ITunnelServiceController
{
    private int _restarts;

    public int Restarts => _restarts;

    public bool Active { get; set; }

    public bool FailRestart { get; set; }

    public Task RestartAsync(CancellationToken cancellationToken)
    {
        if (FailRestart)
        {
            throw new InvalidOperationException("simulated restart failure");
        }

        Interlocked.Increment(ref _restarts);
        Active = true;
        return Task.CompletedTask;
    }

    public Task<bool> IsActiveAsync(CancellationToken cancellationToken) => Task.FromResult(Active);
}
