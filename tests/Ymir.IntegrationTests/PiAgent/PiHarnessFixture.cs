using Microsoft.Extensions.Options;
using Ymir.Testing.FakeLlm;
using Ymir.VibeMaker.Application.Agents;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Infrastructure.PiAgent;
using Ymir.VibeMaker.Infrastructure.Runtime;

namespace Ymir.IntegrationTests.PiAgent;

/// <summary>
/// 真實的 Pi 程序（Local runtime）+ Fake LLM。需要 PATH 上有 <c>pi</c>
/// （<c>npm i -g @earendil-works/pi-coding-agent@1.0.0</c>），否則測試會被略過。
/// </summary>
public sealed class PiHarnessFixture : IAsyncLifetime
{
    public string WorkspaceRoot { get; } = Path.Combine(Path.GetTempPath(), "ymir-pi-it-" + Guid.NewGuid().ToString("N"));

    public FakeLlmServer FakeLlm { get; private set; } = null!;

    public IAgentRuntimeManager RuntimeManager { get; private set; } = null!;

    public bool PiAvailable { get; } = IsOnPath("pi");

    public async ValueTask InitializeAsync()
    {
        FakeLlm = await FakeLlmServer.StartAsync();
        RuntimeManager = new LocalRuntimeManager(
            Options.Create(new RuntimeOptions { Provider = RuntimeProvider.Local, WorkspaceRoot = WorkspaceRoot }),
            new TestOutputLogger<LocalRuntimeManager>());
    }

    public IAgentHarness CreateHarness(bool autoRetry = false) =>
        new PiAgentHarness(
            RuntimeManager,
            Options.Create(new PiAgentOptions
            {
                ModelBaseUrl = FakeLlm.BaseUrl,
                ModelId = FakeLlmEndpoints.ModelId,
                DevelopmentApiKey = "integration-test-key",
                AutoRetry = autoRetry,
                AbortGracePeriod = TimeSpan.FromSeconds(5),
            }),
            new TestOutputLogger<PiAgentHarness>());

    public async ValueTask DisposeAsync()
    {
        await FakeLlm.DisposeAsync();
        if (Directory.Exists(WorkspaceRoot))
        {
            Directory.Delete(WorkspaceRoot, recursive: true);
        }
    }

    private static bool IsOnPath(string executable) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, executable)));
}
