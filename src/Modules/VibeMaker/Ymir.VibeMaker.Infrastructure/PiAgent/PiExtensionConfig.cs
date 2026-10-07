using System.Text.Json;
using System.Text.Json.Nodes;
using Ymir.VibeMaker.Application.Extensions;

namespace Ymir.VibeMaker.Infrastructure.PiAgent;

/// <summary>
/// 依擴充政策組合 Pi 的參數與每次執行前重寫的設定檔（ADR-0012 A.3、B.3）。
/// A0 spike 實測（ADR-0012「Spike 結果」）：
/// <list type="bullet">
/// <item><c>--no-extensions</c> 一律加上：否則 Agent 寫進 agent dir 的 extension（可執行程式碼）會被載入；MCP 只在允許時以 <c>-e builtin:mcp</c> 開回。</item>
/// <item><c>--no-skills</c> 關閉使用者層與專案層 skill；<c>--skill</c> 明列的平台 skill 不受影響。</item>
/// <item>Pi 只從 agent dir 的 <c>mcp.json</c> 讀 MCP 設定、沒有唯讀層，所以由 Ymir 每次執行前產生；
/// <c>settings.json</c> / <c>trust.json</c> 也一併重寫，避免 Agent 把專案設成受信任。</item>
/// </list>
/// </summary>
internal static class PiExtensionConfig
{
    public const string ExtensionBuilderSkillName = "ymir-extension-builder";

    /// <summary>使用者 MCP 設定最多讀取的位元組數；超過視為無效。</summary>
    public const int MaxUserMcpBytes = 256 * 1024;

    /// <summary>不信任任何專案：專案層的 skill、MCP、extension 都不載入（ADR-0012 A.4）。</summary>
    public const string SettingsJson = """{"defaultProjectTrust":"never"}""";

    public const string TrustJson = "{}";

    private static readonly JsonSerializerOptions s_writeOptions = new() { WriteIndented = true };

    public static IReadOnlyList<string> BuildArguments(EffectiveExtensions extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        List<string> arguments = ["--no-extensions"];
        if (!extensions.Skills)
        {
            arguments.Add("--no-skills");
        }

        if (extensions.Mcp)
        {
            arguments.AddRange(["--extension", "builtin:mcp"]);
        }

        if (extensions.Skills || extensions.Mcp)
        {
            arguments.AddRange(["--skill", PiRuntimeLayout.ExtensionBuilderSkillDirectory]);
        }

        return arguments;
    }

    /// <summary>
    /// 產生該次執行的 <c>mcp.json</c>：允許 <c>mcp</c> 時帶入使用者自建的項目，否則為空。
    /// 只取 <c>mcpServers</c>；內容不是合法 JSON 物件時視為沒有（<paramref name="invalid"/> 為 true，由呼叫端記錄）。
    /// 第二階段的平台 MCP 會在這裡合併且優先（ADR-0012 B.6）。
    /// </summary>
    public static string BuildMcpConfig(string? userMcpJson, bool allowUserServers, out bool invalid)
    {
        invalid = false;
        var servers = new JsonObject();
        if (allowUserServers && !string.IsNullOrWhiteSpace(userMcpJson))
        {
            try
            {
                if (JsonNode.Parse(userMcpJson) is JsonObject root && root["mcpServers"] is JsonObject userServers)
                {
                    foreach (var (name, server) in userServers)
                    {
                        if (server is JsonObject)
                        {
                            servers[name] = server.DeepClone();
                        }
                    }
                }
                else
                {
                    invalid = true;
                }
            }
            catch (JsonException)
            {
                invalid = true;
            }
        }

        return new JsonObject { ["mcpServers"] = servers }.ToJsonString(s_writeOptions);
    }

    /// <summary>平台維護的 skill：告訴 Agent 擴充放在哪裡、何時生效與限制（ADR-0012 A.5）。只在至少一項能力開啟時載入。</summary>
    public static string BuildExtensionBuilderSkill(EffectiveExtensions extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        var skills = extensions.Skills
            ? $"""
              ## Skill（已開放）

              - 位置：`{PiRuntimeLayout.AgentDirectory}/skills/<名稱>/SKILL.md`，名稱用小寫英數字與 `-`。
              - `SKILL.md` 開頭要有 frontmatter（`name`、`description`），`description` 說明什麼時候該使用這個 skill。
              - 附帶的腳本與參考檔放在同一個目錄。
              """
            : """
              ## Skill（未開放）

              管理員沒有開放自建 skill。使用者要求時，請說明目前沒有這個權限，請洽管理員；不要用其他方式繞過。
              """;
        var mcp = extensions.Mcp
            ? $$"""
              ## MCP server（已開放）

              - 設定寫在 `{{PiRuntimeLayout.AgentDirectory}}/{{PiRuntimeLayout.UserMcpFileName}}`，格式為 `{ "mcpServers": { "<名稱>": { ... } } }`
                （stdio：`command`、`args`；HTTP：`url`）。
              - **不要**直接修改 `mcp.json`：它由平台每次執行前產生，修改會被覆蓋。
              - 名稱與平台提供的服務相同時，以平台的為準。
              """
            : """
              ## MCP server（未開放）

              管理員沒有開放自建 MCP server。使用者要求時，請說明目前沒有這個權限，請洽管理員；不要用其他方式繞過。
              """;
        return $"""
            ---
            name: {ExtensionBuilderSkillName}
            description: 使用者要求建立、修改或刪除自己的 skill 或 MCP server 時使用；說明 Ymir 平台上擴充的放置位置、生效時機與限制。
            ---

            # 在 Ymir 建立自己的擴充

            這些擴充只屬於目前的使用者，只在這位使用者自己的執行環境中執行。

            {skills}
            {mcp}
            ## 共同規則

            - 新增或修改後，**從下一則訊息開始**才會生效；告訴使用者這一點。
            - 不要要求使用者在對話中貼上密碼、token 或 API key；需要憑證時請使用者洽管理員，或改用環境變數名稱引用。
            - 不要修改 `{PiRuntimeLayout.AgentDirectory}` 下的 `settings.json`、`trust.json`、`models.json`、`mcp.json`，也不要建立 `extensions/`：這些由平台管理，會被覆蓋或不會載入。
            """;
    }
}
