using Microsoft.Extensions.Options;
using Ymir.Testing.FakeLlm;
using Ymir.VibeMaker.Application.Agents;
using Ymir.VibeMaker.Application.Models;
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
    /// <summary>直接呼叫 harness 時使用的模型金鑰（Fake LLM 不驗證）。</summary>
    public const string ModelApiKey = "integration-test-key";

    /// <summary>第二個可選用的模型（Fake LLM 不檢查模型名稱，用來驗證 --model 確實帶入）。</summary>
    public const string SecondModelId = "fake-model-2";

    public static ModelCatalog Catalog { get; } = new(
        [new ModelDescriptor(FakeLlmEndpoints.ModelId, "Fake"), new ModelDescriptor(SecondModelId, "Fake 2")],
        FakeLlmEndpoints.ModelId);

    /// <summary>直接呼叫 harness 的 request（預設模型、沒有附加 system prompt）。</summary>
    public static AgentRunRequest Request(Guid runtimeId, Guid sessionId, string prompt, string? workingDirectory = null, string? modelId = null, IReadOnlyList<string>? systemPrompts = null) =>
        new(Guid.NewGuid(), runtimeId, sessionId, prompt, workingDirectory ?? RuntimePaths.Workspace, ModelApiKey, modelId ?? FakeLlmEndpoints.ModelId, systemPrompts ?? []);

    public string WorkspaceRoot { get; } = Path.Combine(Path.GetTempPath(), "ymir-pi-it-" + Guid.NewGuid().ToString("N"));

    public FakeLlmServer FakeLlm { get; private set; } = null!;

    public IAgentRuntimeManager RuntimeManager { get; private set; } = null!;

    public bool PiAvailable { get; } = IsPiOnPath();

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
                DevelopmentApiKey = ModelApiKey,
                AutoRetry = autoRetry,
                AbortGracePeriod = TimeSpan.FromSeconds(5),
            }),
            Catalog,
            new TestOutputLogger<PiAgentHarness>());

    public async ValueTask DisposeAsync()
    {
        await FakeLlm.DisposeAsync();
        if (Directory.Exists(WorkspaceRoot))
        {
            Directory.Delete(WorkspaceRoot, recursive: true);
        }
    }

    public static bool IsPiOnPath() => IsOnPath("pi");

    private static bool IsOnPath(string executable) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, executable)));
}
