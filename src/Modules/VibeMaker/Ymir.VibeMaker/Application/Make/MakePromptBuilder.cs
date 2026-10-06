using System.Globalization;
using System.Text;

namespace Ymir.VibeMaker.Application.Make;

/// <summary>
/// <c>/make</c> 指令：解析使用者輸入，並組合送給 Agent 的完整指示。純函式，方便測試。
/// <list type="bullet">
/// <item>點主題按鈕：Agent 先問需求，不急著建檔。</item>
/// <item><c>/make &lt;描述&gt;</c>：Agent 依描述判斷最適合的主題，再依該主題的指示進行。</item>
/// </list>
/// </summary>
public static class MakePromptBuilder
{
    public const string Command = "/make";

    /// <summary>不是 <c>/make</c> 時回傳 null；只有 <c>/make</c> 時回傳空字串；否則回傳描述。</summary>
    public static string? ParseDescription(string? content)
    {
        var text = content?.Trim();
        if (text is null || !text.StartsWith(Command, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = text[Command.Length..];
        // "/maker" 之類不算指令：/make 後面必須是結尾或空白。
        if (rest.Length > 0 && !char.IsWhiteSpace(rest[0]))
        {
            return null;
        }

        return rest.Trim();
    }

    /// <summary>點了主題按鈕：說明主題與建置指示，並要求先問需求。</summary>
    public static string ForTopic(MakeTopicPrompt topic, string? extraDescription = null)
    {
        ArgumentNullException.ThrowIfNull(topic);
        var builder = new StringBuilder();
        builder.AppendLine("[/make] 使用者想建置的主題：" + topic.Name);
        AppendTopicDetails(builder, topic, indent: string.Empty);
        if (!string.IsNullOrWhiteSpace(extraDescription))
        {
            builder.AppendLine("使用者補充：" + extraDescription.Trim());
        }

        builder.AppendLine();
        builder.Append("請先不要建立任何檔案。先用條列的方式問使用者 3～5 個最關鍵的問題（例如用途、主要功能、使用的人、外觀偏好），等使用者回答後，再依上面的建置指示開始建置。請用繁體中文。");
        return builder.ToString();
    }

    /// <summary><c>/make &lt;描述&gt;</c>：列出可選的主題，請 Agent 判斷最適合的一個。</summary>
    public static string ForDescription(string description, IReadOnlyList<MakeTopicPrompt> topics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(topics);
        var builder = new StringBuilder();
        builder.AppendLine("[/make] 使用者想建置：" + description.Trim());
        builder.AppendLine();
        if (topics.Count == 0)
        {
            builder.Append("請依使用者的描述在目前的工作目錄建置可以直接使用的成果；需求不清楚的地方先問使用者。完成後說明建立了哪些檔案與使用方式。請用繁體中文。");
            return builder.ToString();
        }

        builder.AppendLine("可選的主題：");
        for (var i = 0; i < topics.Count; i++)
        {
            builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{i + 1}. {topics[i].Name}"));
            AppendTopicDetails(builder, topics[i], indent: "   ");
        }

        builder.AppendLine();
        builder.Append("請先判斷最適合的主題，第一行用「主題：<名稱>」告訴使用者你的判斷，並附一句理由；接著依該主題的建置指示進行。需求不清楚的地方先問使用者，不要自行假設太多。請用繁體中文。");
        return builder.ToString();
    }

    private static void AppendTopicDetails(StringBuilder builder, MakeTopicPrompt topic, string indent)
    {
        if (!string.IsNullOrWhiteSpace(topic.Description))
        {
            builder.AppendLine(indent + "主題說明：" + topic.Description);
        }

        builder.AppendLine(indent + "建置指示：" + topic.Instructions);
    }
}

/// <summary>組合 prompt 需要的主題內容。</summary>
public sealed record MakeTopicPrompt(string Name, string? Description, string Instructions);
