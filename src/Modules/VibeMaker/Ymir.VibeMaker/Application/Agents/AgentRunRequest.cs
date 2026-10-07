namespace Ymir.VibeMaker.Application.Agents;

/// <param name="SessionId">AGENT_SESSION.id；harness 以此續接同一個 Agent 上下文。</param>
/// <param name="WorkingDirectory">Agent 的工作目錄（runtime 內部路徑，見 <see cref="Runtime.RuntimePaths.WorkingDirectoryFor"/>）。</param>
/// <param name="ModelApiKey">使用者的 LiteLLM virtual key（ADR-0004）；只以環境變數名稱傳進 runtime，不得出現在程序參數或 log。</param>
/// <param name="ModelId">這次使用的模型（LiteLLM model_name）。</param>
/// <param name="SystemPrompts">依序附加在 Agent 預設 system prompt 之後的內容（個人 → 專案），可為空。</param>
/// <param name="Extensions">這次允許的使用者擴充能力（ADR-0012 A.3）；null 視為全部關閉。</param>
/// <param name="Attachments">使用者附加的檔案（已在工作目錄內）；harness 可把圖片直接附給支援視覺的模型。</param>
public sealed record AgentRunRequest(
    Guid ExecutionId,
    Guid RuntimeId,
    Guid SessionId,
    string Prompt,
    string WorkingDirectory,
    string ModelApiKey,
    string ModelId,
    IReadOnlyList<string> SystemPrompts,
    Extensions.EffectiveExtensions? Extensions = null,
    IReadOnlyList<AgentAttachment>? Attachments = null)
{
    public IReadOnlyList<AgentAttachment> Attachments { get; init; } = Attachments ?? [];

    /// <summary>record 預設的 ToString 會印出金鑰與 prompt；log 只需要識別資訊。</summary>
    public override string ToString() =>
        $"AgentRunRequest {{ ExecutionId = {ExecutionId}, RuntimeId = {RuntimeId}, SessionId = {SessionId}, WorkingDirectory = {WorkingDirectory}, ModelId = {ModelId}, SystemPrompts = {SystemPrompts.Count}, Extensions = {Extensions}, Attachments = {Attachments.Count} }}";
}

/// <param name="Path">工作目錄內的相對路徑。</param>
/// <param name="ContentType">伺服器以檔頭判斷的類型。</param>
public sealed record AgentAttachment(string Path, string ContentType, long Size);
