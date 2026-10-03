using System.Runtime.CompilerServices;
using Ymir.VibeMaker.Application.Agents;

namespace Ymir.VibeMaker.Infrastructure.Dev;

/// <summary>
/// 不需要 Pi / LLM 的假 harness：輸出固定腳本的事件，讓前端與 SSE 管線可以在沒有 runtime 的情況下開發與測試。
/// </summary>
internal sealed class ScriptedAgentHarness : IAgentHarness
{
    internal static TimeSpan StepDelay { get; set; } = TimeSpan.FromMilliseconds(150);

    public async IAsyncEnumerable<AgentEvent> RunAsync(
        AgentRunRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var text = string.Empty;
        AgentEvent[] script =
        [
            new AgentStarted(),
            new AgentStatus("（示範模式）正在準備 Runtime"),
            new AgentTextDelta("收到你的訊息："),
            new AgentTextDelta(request.Prompt),
            new AgentToolStarted("bash", "demo-call-1", "ls -la"),
            new AgentToolCompleted("demo-call-1", true),
            new AgentTextDelta("\n\n這是示範回應，尚未連接真正的 Agent。"),
        ];

        foreach (var agentEvent in script)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield return new AgentCancelled(text);
                yield break;
            }

            if (agentEvent is AgentTextDelta delta)
            {
                text += delta.Text;
            }

            yield return agentEvent;
            await DelayAsync(cancellationToken).ConfigureAwait(false);
        }

        yield return cancellationToken.IsCancellationRequested ? new AgentCancelled(text) : new AgentCompleted(text);
    }

    private static async Task DelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(StepDelay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 下一輪迴圈會輸出 AgentCancelled。
        }
    }
}
