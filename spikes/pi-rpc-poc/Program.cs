using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ymir.Testing.FakeLlm;
using Ymir.VibeMaker.Application.Agents;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Infrastructure;

namespace Ymir.Spikes.PiRpcPoc;

/// <summary>
/// Sprint 0 技術驗證：Runtime（Local / Podman）→ Pi RPC → 模型端點，印出映射後的 SSE 事件。
/// 用法見 spikes/pi-rpc-poc/README.md。
/// </summary>
internal static class Program
{
    private static readonly Guid DefaultWorkspaceId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid DefaultSessionId = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    public static async Task<int> Main(string[] args)
    {
        var options = PocOptions.Parse(args);

        await using var fakeLlm = options.LlmUrl is null ? await FakeLlmServer.StartAsync() : null;
        var modelBaseUrl = options.LlmUrl ?? fakeLlm!.BaseUrl.ToString();
        Console.WriteLine($"# runtime={options.Runtime} model={options.Model} endpoint={modelBaseUrl}");
        Console.WriteLine($"# workspace={options.WorkspaceId} session={options.SessionId}");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VibeMaker:Harness"] = "Pi",
                ["VibeMaker:Runtime:Provider"] = options.Runtime,
                ["VibeMaker:Runtime:WorkspaceRoot"] = options.WorkspaceRoot,
                ["VibeMaker:Runtime:Image"] = options.Image,
                ["VibeMaker:Runtime:Network"] = options.Network,
                ["VibeMaker:Pi:ModelBaseUrl"] = modelBaseUrl,
                ["VibeMaker:Pi:ModelId"] = options.Model,
                ["VibeMaker:Pi:DevelopmentApiKey"] = options.ApiKey,
                ["VibeMaker:Pi:AutoRetry"] = "false",
            })
            .Build();

        var services = new ServiceCollection()
            .AddLogging(logging => logging.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information))
            .AddVibeMakerAgentRuntime(configuration, isDevelopment: true)
            .BuildServiceProvider();
        await using var _ = services;

        var runtimeManager = services.GetRequiredService<IAgentRuntimeManager>();
        var harness = services.GetRequiredService<IAgentHarness>();

        var runtime = await runtimeManager.EnsureRuntimeAsync(options.WorkspaceId, CancellationToken.None);
        Console.WriteLine($"# runtime ready: {runtime.Provider} {runtime.ProviderRuntimeId}");

        var prompts = options.Prompts.Count > 0 ? options.Prompts : ReadPromptsInteractively();
        foreach (var prompt in prompts)
        {
            await RunOnceAsync(harness, runtime.RuntimeId, options.SessionId, prompt);
        }

        return 0;
    }

    private static async Task RunOnceAsync(IAgentHarness harness, Guid runtimeId, Guid sessionId, string prompt)
    {
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("# Ctrl+C → abort");
            cts.Cancel();
        };
        Console.CancelKeyPress += onCancel;
        try
        {
            var executionId = Guid.NewGuid();
            Console.WriteLine($"\n> {prompt}");
            var sequence = 0;
            await foreach (var agentEvent in harness.RunAsync(new AgentRunRequest(executionId, runtimeId, sessionId, prompt), cts.Token))
            {
                var executionEvent = agentEvent.ToExecutionEvent(executionId);
                Console.WriteLine($"id: {++sequence}\nevent: {executionEvent.EventName}\ndata: {executionEvent.ToJson()}\n");
            }
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    private static IEnumerable<string> ReadPromptsInteractively()
    {
        Console.WriteLine("# 輸入訊息後按 Enter；空白行結束。Fake LLM 指令：[create-file] [slow] [fail]");
        while (true)
        {
            Console.Write("prompt> ");
            var line = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                yield break;
            }

            yield return line;
        }
    }

    private sealed record PocOptions(
        string Runtime,
        string? LlmUrl,
        string Model,
        string ApiKey,
        string WorkspaceRoot,
        string Image,
        string Network,
        Guid WorkspaceId,
        Guid SessionId,
        List<string> Prompts)
    {
        public static PocOptions Parse(string[] args)
        {
            string? Value(string name)
            {
                var index = Array.IndexOf(args, name);
                return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
            }

            var prompts = new List<string>();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--prompt")
                {
                    prompts.Add(args[i + 1]);
                }
            }

            var llmUrl = Value("--llm-url");
            return new PocOptions(
                Runtime: Value("--runtime") ?? "Local",
                LlmUrl: llmUrl,
                Model: Value("--model") ?? (llmUrl is null ? FakeLlmEndpoints.ModelId : "default"),
                ApiKey: Value("--api-key") ?? Environment.GetEnvironmentVariable("LITELLM_API_KEY") ?? "dev-key",
                WorkspaceRoot: Value("--workspace-root") ?? Path.Combine(Path.GetTempPath(), "ymir-poc-workspaces"),
                Image: Value("--image") ?? "localhost/ymir/agent-runtime:dev",
                Network: Value("--network") ?? "slirp4netns",
                WorkspaceId: Guid.TryParse(Value("--workspace"), out var workspaceId) ? workspaceId : DefaultWorkspaceId,
                SessionId: Guid.TryParse(Value("--session"), out var sessionId) ? sessionId : DefaultSessionId,
                Prompts: prompts);
        }
    }
}
