using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace Ymir.VibeMaker.Contracts.Executions;

/// <summary>
/// 推送給瀏覽器的 execution 事件（SSE <c>data:</c> 內容）。
/// 只能放摘要：不得包含 secret、完整環境變數、Authorization header 或完整 command output（SA §10 安全規則）。
/// </summary>
/// <param name="EventName">SSE <c>event:</c> 欄位，不序列化進 data。</param>
public abstract record ExecutionEvent([property: JsonIgnore] string EventName)
{
    // 不轉義中日韓等文字（SSE data 不會嵌入 HTML），但仍轉義 HTML 敏感字元。
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    /// <summary>以實際型別序列化為 SSE data（camelCase JSON）。</summary>
    public string ToJson() => JsonSerializer.Serialize(this, GetType(), s_jsonOptions);
}

public sealed record ExecutionStartedEvent(Guid ExecutionId)
    : ExecutionEvent(ExecutionEventNames.ExecutionStarted);

public sealed record AssistantDeltaEvent(string Text)
    : ExecutionEvent(ExecutionEventNames.AssistantDelta);

public sealed record ToolStartedEvent(string Tool, string CallId, string Summary)
    : ExecutionEvent(ExecutionEventNames.ToolStarted);

public sealed record ToolCompletedEvent(string CallId, bool Success)
    : ExecutionEvent(ExecutionEventNames.ToolCompleted);

public sealed record StatusEvent(string Text)
    : ExecutionEvent(ExecutionEventNames.Status);

public sealed record ExecutionCompletedEvent(Guid ExecutionId, Guid? MessageId)
    : ExecutionEvent(ExecutionEventNames.ExecutionCompleted);

public sealed record ExecutionFailedEvent(string Code, string Message)
    : ExecutionEvent(ExecutionEventNames.ExecutionFailed);

public sealed record ExecutionCancelledEvent(Guid ExecutionId)
    : ExecutionEvent(ExecutionEventNames.ExecutionCancelled);
