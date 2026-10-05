namespace Ymir.VibeMaker.Application.Agents;

/// <param name="SessionId">AGENT_SESSION.id；harness 以此續接同一個 Agent 上下文。</param>
/// <param name="WorkingDirectory">Agent 的工作目錄（runtime 內部路徑，見 <see cref="Runtime.RuntimePaths.WorkingDirectoryFor"/>）。</param>
public sealed record AgentRunRequest(Guid ExecutionId, Guid RuntimeId, Guid SessionId, string Prompt, string WorkingDirectory);
