namespace Ymir.VibeMaker.Application.Agents;

/// <summary>Agent loop 的抽象（MVP 實作為 Pi，SA §19）。Application 層不得直接依賴 Pi 協定。</summary>
public interface IAgentHarness
{
    /// <summary>
    /// 執行一次 prompt 並串流事件。<paramref name="cancellationToken"/> 被取消時，harness 必須中止 Agent
    /// 並以 <see cref="AgentCancelled"/> 結束串流（不拋出 <see cref="OperationCanceledException"/>）。
    /// </summary>
    IAsyncEnumerable<AgentEvent> RunAsync(AgentRunRequest request, CancellationToken cancellationToken);
}
