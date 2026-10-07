using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Ymir.Testing.FakeLlm;

/// <summary>
/// 依照最後一則訊息決定假模型的回應，讓測試可以腳本化 Agent 行為：
/// <list type="bullet">
/// <item>最後一則是 tool 結果 → 回覆「完成」文字。</item>
/// <item>使用者訊息含 <c>[create-file]</c> → 呼叫 <c>bash</c> 工具在 workspace 建立 <c>hello.txt</c>。</item>
/// <item>使用者訊息含 <c>[slow]</c> → 緩慢串流很多段文字（用來測 abort）。</item>
/// <item>使用者訊息含 <c>[markdown]</c> → 分段串流一段 Markdown（標題、程式碼區塊、清單、表格），測前端排版。</item>
/// <item>使用者訊息含 <c>[fail]</c> → 回傳 HTTP 500（測模型端錯誤）。</item>
/// <item>其他：回覆收到的文字；最後一則使用者訊息附有圖片時，加上 <see cref="ImageCountPrefix"/> 與張數。</item>
/// <item>其他 → 回覆「收到第 N 則使用者訊息：...」，N 可用來驗證 session 續接。</item>
/// </list>
/// </summary>
public static partial class FakeLlmScript
{
    public const string CreateFileMarker = "[create-file]";
    public const string SlowMarker = "[slow]";
    public const string FailMarker = "[fail]";
    public const string MarkdownMarker = "[markdown]";
    public const string CreatedFileName = "hello.txt";
    public const string CreatedFileContent = "Hello from Ymir";

    public static FakeLlmReply Decide(JsonObject request)
    {
        var messages = request["messages"]?.AsArray() ?? [];
        var last = messages.LastOrDefault()?.AsObject();
        var lastRole = last?["role"]?.GetValue<string>();
        var userMessages = messages.Where(m => m?["role"]?.GetValue<string>() == "user").ToList();
        var lastUserText = userMessages.Count > 0 ? ExtractText(userMessages[^1]) : string.Empty;

        if (lastRole == "tool")
        {
            return FakeLlmReply.Text(["已完成，", $"檔案 {CreatedFileName} 已建立。"]);
        }

        if (lastUserText.Contains(FailMarker, StringComparison.Ordinal))
        {
            return FakeLlmReply.Error(500, "fake upstream failure");
        }

        if (lastUserText.Contains(CreateFileMarker, StringComparison.Ordinal))
        {
            var systemText = string.Join("\n", messages.Where(m => m?["role"]?.GetValue<string>() == "system").Select(ExtractText));
            var delivery = DeliveryDirectory().Matches(systemText).LastOrDefault()?.Value;
            var command = delivery is null ? $"printf '{CreatedFileContent}' > {CreatedFileName}"
                : $"mkdir -p {delivery} && printf '{CreatedFileContent}' > {delivery}/{CreatedFileName}";
            return FakeLlmReply.ToolCall(
                "bash",
                new JsonObject { ["command"] = command },
                preamble: "我來建立檔案。");
        }

        if (lastUserText.Contains(SlowMarker, StringComparison.Ordinal))
        {
            return FakeLlmReply.Text([.. Enumerable.Range(1, 200).Select(i => $"第{i}段 ")], delayPerChunk: TimeSpan.FromMilliseconds(100));
        }

        if (lastUserText.Contains(MarkdownMarker, StringComparison.Ordinal))
        {
            // 分段點刻意切在程式碼區塊中間，讓前端經歷「未閉合 fence」的串流狀態
            return FakeLlmReply.Text(
            [
                "## 啟用本機管理員帳戶\n\n",
                "### Windows 命令提示字元（以**系統管理員**身分執行）\n\n```cmd\nnet user Admin",
                "istrator /active:yes\n```\n\n",
                "- 設定密碼：`net user Administrator 你的密碼`\n- 停用：`net user Administrator /active:no`\n\n",
                "| 指令 | 用途 |\n|---|---|\n| `/active:yes` | 啟用 |\n| `/active:no` | 停用 |\n",
            ],
                delayPerChunk: TimeSpan.FromMilliseconds(150));
        }

        // 附加的圖片以 OpenAI 的 image_url content part 送來；回報張數讓測試確認圖片有送到模型。
        var images = userMessages.Count > 0 ? CountImages(userMessages[^1]) : 0;
        return images > 0
            ? FakeLlmReply.Text([$"收到第 {userMessages.Count} 則使用者訊息：", lastUserText, $"{ImageCountPrefix}{images}"])
            : FakeLlmReply.Text([$"收到第 {userMessages.Count} 則使用者訊息：", lastUserText]);
    }

    /// <summary>回覆中「收到圖片：N」的前綴。</summary>
    public const string ImageCountPrefix = "（收到圖片）";

    [GeneratedRegex("deliverables/[0-9a-f]{32}", RegexOptions.CultureInvariant)]
    private static partial Regex DeliveryDirectory();

    private static int CountImages(JsonNode? message) =>
        message?["content"] is JsonArray parts ? parts.Count(p => p?["type"]?.GetValue<string>() == "image_url") : 0;

    private static string ExtractText(JsonNode? message)
    {
        var content = message?["content"];
        return content switch
        {
            JsonValue value => value.GetValue<string>(),
            JsonArray parts => string.Concat(parts.Select(p => p?["text"]?.GetValue<string>() ?? string.Empty)),
            _ => string.Empty,
        };
    }
}

public sealed record FakeLlmReply
{
    public IReadOnlyList<string> TextChunks { get; init; } = [];

    public TimeSpan DelayPerChunk { get; init; }

    public string? ToolName { get; init; }

    public JsonObject? ToolArguments { get; init; }

    public int? ErrorStatusCode { get; init; }

    public string? ErrorMessage { get; init; }

    public static FakeLlmReply Text(IReadOnlyList<string> chunks, TimeSpan delayPerChunk = default) =>
        new() { TextChunks = chunks, DelayPerChunk = delayPerChunk };

    public static FakeLlmReply ToolCall(string toolName, JsonObject arguments, string? preamble = null) =>
        new() { ToolName = toolName, ToolArguments = arguments, TextChunks = preamble is null ? [] : [preamble] };

    public static FakeLlmReply Error(int statusCode, string message) =>
        new() { ErrorStatusCode = statusCode, ErrorMessage = message };
}
