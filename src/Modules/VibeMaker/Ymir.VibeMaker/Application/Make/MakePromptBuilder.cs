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
        builder.Append("請先不要建立任何檔案。依主題的建置指示，先用條列的方式問使用者 3～5 個最關鍵的問題；已提供的資訊不要重複詢問。等使用者回答後，再依上面的建置指示開始建置。請用繁體中文。");
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
        if (topic.ProducesTool)
        {
            builder.AppendLine(indent + ToolInstruction);
        }
    }

    /// <summary>
    /// 產生小工具的主題一律附加的平台規定（ADR-0029）：成果放在固定位置，平台才能自動發布到左側「小工具」。
    /// 主題的建置指示由 Admin 編輯，這段不交給 Admin 維護，避免改壞發布位置。
    /// </summary>
    public const string ToolInstruction =
        "平台規定：這是只有前端的網頁小工具，請把成果放在工作目錄的 tool/ 目錄，主頁面必須是 tool/index.html（其他 CSS、JavaScript、圖片也放在 tool/ 內，用相對路徑引用）；"
        + "不需要後端或資料庫，也不要依賴需要登入的外部服務。完成後平台會自動把它放到左側選單的「小工具」，使用者可以直接開啟。";
}

/// <summary>組合 prompt 需要的主題內容。</summary>
/// <param name="ProducesTool">產生前端小工具（ADR-0029）。</param>
public sealed record MakeTopicPrompt(string Name, string? Description, string Instructions, bool ProducesTool = false);
