namespace Ymir.VibeMaker.Application.Agents;

/// <param name="SessionId">AGENT_SESSION.id；harness 以此續接同一個 Agent 上下文。</param>
public sealed record AgentRunRequest(Guid ExecutionId, Guid RuntimeId, Guid SessionId, string Prompt);
