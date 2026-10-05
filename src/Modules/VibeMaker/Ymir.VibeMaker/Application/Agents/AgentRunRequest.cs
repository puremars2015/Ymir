namespace Ymir.VibeMaker.Application.Agents;

/// <param name="SessionId">AGENT_SESSION.id；harness 以此續接同一個 Agent 上下文。</param>
/// <param name="WorkingDirectory">Agent 的工作目錄（runtime 內部路徑，見 <see cref="Runtime.RuntimePaths.WorkingDirectoryFor"/>）。</param>
/// <param name="ModelApiKey">使用者的 LiteLLM virtual key（ADR-0004）；只以環境變數名稱傳進 runtime，不得出現在程序參數或 log。</param>
public sealed record AgentRunRequest(Guid ExecutionId, Guid RuntimeId, Guid SessionId, string Prompt, string WorkingDirectory, string ModelApiKey)
{
    /// <summary>record 預設的 ToString 會印出金鑰與 prompt；log 只需要識別資訊。</summary>
    public override string ToString() =>
        $"AgentRunRequest {{ ExecutionId = {ExecutionId}, RuntimeId = {RuntimeId}, SessionId = {SessionId}, WorkingDirectory = {WorkingDirectory} }}";
}
