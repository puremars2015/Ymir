namespace Ymir.VibeMaker.Application.Agents;

/// <summary>
/// Harness 無關的 Agent 事件。各 harness（Pi、未來其他）把自己的協定轉成這些事件，
/// Application 層再轉成對外的 SSE 事件（<see cref="ExecutionEventTranslator"/>）。
/// 每次 run 的最後一個事件必定是 <see cref="AgentCompleted"/>、<see cref="AgentFailed"/> 或 <see cref="AgentCancelled"/> 其中之一。
/// </summary>
public abstract record AgentEvent;

public sealed record AgentStarted : AgentEvent;

public sealed record AgentTextDelta(string Text) : AgentEvent;

/// <param name="Summary">給使用者看的摘要（例如截斷後的指令），不可含 secret 或完整輸出。</param>
public sealed record AgentToolStarted(string Tool, string CallId, string Summary) : AgentEvent;

public sealed record AgentToolCompleted(string CallId, bool Success) : AgentEvent;

public sealed record AgentStatus(string Text) : AgentEvent;

/// <param name="FinalText">最後一則 assistant 訊息的完整文字，用來保存 ASSISTANT MESSAGE。</param>
public sealed record AgentCompleted(string FinalText) : AgentEvent;

/// <param name="Code">SA §13 錯誤碼。</param>
/// <param name="Message">可顯示給使用者的訊息，不可含 host path、stack trace、token。</param>
public sealed record AgentFailed(string Code, string Message) : AgentEvent;

/// <param name="PartialText">中止前已產生的文字。</param>
public sealed record AgentCancelled(string PartialText) : AgentEvent;
