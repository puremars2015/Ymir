using System.Globalization;
using System.Text;
using System.Text.Json;
using Ymir.VibeMaker.Application.Agents;
using Ymir.VibeMaker.Contracts.Executions;

namespace Ymir.VibeMaker.Infrastructure.PiAgent;

/// <summary>
/// 把 Pi RPC 的 stdout session event 轉成 <see cref="AgentEvent"/>（對應表見 ADR-0003）。
/// 每次 run 建立一個新的 instance（有狀態：追蹤最後一則 assistant 訊息的 stopReason 與累積文字）。
/// </summary>
internal sealed class PiRpcEventMapper
{
    internal const int MaxSummaryLength = 120;
    internal const string AssistantMessageSeparator = "\n\n";

    private readonly StringBuilder _text = new();
    private bool _started;
    private bool _assistantMessageHasText;
    private string? _lastStopReason;

    /// <summary>最後一次模型錯誤的原始訊息，只能寫入 server log，不可回傳給瀏覽器。</summary>
    public string? LastErrorDetail { get; private set; }

    /// <summary>已產生終止事件（completed / failed / cancelled）。</summary>
    public bool IsTerminal { get; private set; }

    /// <summary>目前累積的 assistant 文字（等同使用者在畫面上看到的內容）。</summary>
    public string AccumulatedText => _text.ToString();

    /// <summary>處理一筆 stdout record；<c>response</c> 類型由呼叫端處理，這裡會忽略。</summary>
    public IReadOnlyList<AgentEvent> Map(JsonElement record)
    {
        if (IsTerminal || !record.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            return [];
        }

        return typeElement.GetString() switch
        {
            "agent_start" => OnAgentStart(),
            "message_start" => OnMessageStart(record),
            "message_update" => OnMessageUpdate(record),
            "message_end" => OnMessageEnd(record),
            "tool_execution_start" => OnToolStart(record),
            "tool_execution_end" => OnToolEnd(record),
            "auto_retry_start" => OnAutoRetryStart(record),
            "auto_retry_end" => OnAutoRetryEnd(record),
            "compaction_start" => [new AgentStatus("正在整理對話上下文…")],
            "agent_settled" => OnSettled(),
            _ => [],
        };
    }

    /// <summary>Pi 未送出 <c>agent_settled</c> 就結束時（程序異常），由呼叫端產生終止事件。</summary>
    public AgentEvent CreateUnexpectedExitEvent()
    {
        IsTerminal = true;
        return new AgentFailed(ExecutionErrorCodes.AgentRuntimeError, "Agent 執行程序異常結束。");
    }

    private AgentEvent[] OnAgentStart()
    {
        if (_started)
        {
            return [];
        }

        _started = true;
        return [new AgentStarted()];
    }

    private AgentEvent[] OnMessageStart(JsonElement record)
    {
        if (IsAssistantMessage(record))
        {
            _assistantMessageHasText = false;
        }

        return [];
    }

    private AgentEvent[] OnMessageUpdate(JsonElement record)
    {
        if (!record.TryGetProperty("assistantMessageEvent", out var update)
            || GetString(update, "type") != "text_delta"
            || GetString(update, "delta") is not { Length: > 0 } delta)
        {
            return [];
        }

        // 同一次 run 的多則 assistant 訊息（tool call 前後）之間加上空行，讓串流與保存的內容一致。
        if (!_assistantMessageHasText && _text.Length > 0)
        {
            delta = AssistantMessageSeparator + delta;
        }

        _assistantMessageHasText = true;
        _text.Append(delta);
        return [new AgentTextDelta(delta)];
    }

    private AgentEvent[] OnMessageEnd(JsonElement record)
    {
        if (IsAssistantMessage(record))
        {
            var message = record.GetProperty("message");
            _lastStopReason = GetString(message, "stopReason");
            if (_lastStopReason == "error")
            {
                LastErrorDetail = GetString(message, "errorMessage");
            }
        }

        return [];
    }

    private static AgentEvent[] OnToolStart(JsonElement record)
    {
        var toolName = GetString(record, "toolName") ?? "tool";
        var callId = GetString(record, "toolCallId") ?? string.Empty;
        record.TryGetProperty("args", out var args);
        return [new AgentToolStarted(toolName, callId, SummarizeToolCall(toolName, args))];
    }

    private static AgentEvent[] OnToolEnd(JsonElement record)
    {
        var callId = GetString(record, "toolCallId") ?? string.Empty;
        var isError = record.TryGetProperty("isError", out var e) && e.ValueKind == JsonValueKind.True;
        return [new AgentToolCompleted(callId, !isError)];
    }

    private static AgentEvent[] OnAutoRetryStart(JsonElement record)
    {
        var attempt = GetInt(record, "attempt");
        var max = GetInt(record, "maxAttempts");
        return [new AgentStatus(string.Create(CultureInfo.InvariantCulture, $"模型服務暫時無法回應，正在重試（{attempt}/{max}）…"))];
    }

    private AgentEvent[] OnAutoRetryEnd(JsonElement record)
    {
        if (record.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
        {
            LastErrorDetail = GetString(record, "finalError") ?? LastErrorDetail;
        }

        return [];
    }

    private AgentEvent[] OnSettled()
    {
        IsTerminal = true;
        return _lastStopReason switch
        {
            "aborted" => [new AgentCancelled(AccumulatedText)],
            // LiteLLM 超過使用者預算時回 budget exceeded（ADR-0011）；只回摘要，不含原始錯誤（SA §12）。
            "error" when LastErrorDetail?.Contains("budget", StringComparison.OrdinalIgnoreCase) == true =>
                [new AgentFailed(ExecutionErrorCodes.ModelProviderError, "本月模型預算已用完，請聯絡管理員。")],
            "error" => [new AgentFailed(ExecutionErrorCodes.ModelProviderError, "模型服務發生錯誤，請稍後再試。")],
            _ => [new AgentCompleted(AccumulatedText)],
        };
    }

    /// <summary>工具摘要：只顯示指令或路徑，絕不包含檔案內容或輸出（SA §10 安全規則）。</summary>
    internal static string SummarizeToolCall(string toolName, JsonElement args)
    {
        var summary = toolName switch
        {
            "bash" => GetString(args, "command"),
            "read" or "write" or "edit" => GetString(args, "path"),
            "grep" or "find" => GetString(args, "pattern"),
            "ls" => GetString(args, "path") ?? ".",
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(summary))
        {
            return toolName;
        }

        var firstLine = summary.Split('\n', 2)[0].Trim();
        return Truncate(firstLine, MaxSummaryLength);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        var cut = maxLength - 1;
        if (char.IsHighSurrogate(value[cut - 1]))
        {
            cut--;
        }

        return string.Concat(value.AsSpan(0, cut), "…");
    }

    private static bool IsAssistantMessage(JsonElement record) =>
        record.TryGetProperty("message", out var message) && GetString(message, "role") == "assistant";

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : 0;
}
