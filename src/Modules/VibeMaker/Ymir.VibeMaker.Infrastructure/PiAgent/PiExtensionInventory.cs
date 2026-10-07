using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ymir.VibeMaker.Application.Extensions;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Infrastructure.PiAgent;

/// <summary>
/// 在使用者的 runtime 內列出 Pi agent dir 的自建 skill 與 <c>mcp.user.json</c> 的 server 名稱（ADR-0012 A.6）。
/// 只回名稱：MCP 設定可能含有使用者自己的憑證，內容不離開 runtime。
/// </summary>
internal sealed class PiExtensionInventory(IAgentRuntimeManager runtimes) : IExtensionInventory
{
    public const int MaxEntries = 200;
    public const int MaxNameLength = 100;

    /// <summary>輸出：每個 skill 一行 <c>skill\t名稱</c>；有 MCP 設定時一行 <c>mcp</c>，之後是檔案內容（有上限）。</summary>
    internal const string Script =
        "cd \"$1\" 2>/dev/null || exit 0\n" +
        "for d in skills/*/; do [ -f \"${d}SKILL.md\" ] || continue; n=${d#skills/}; printf 'skill\\t%s\\n' \"${n%/}\"; done\n" +
        "if [ -f \"$2\" ]; then printf 'mcp\\n'; head -c \"$3\" \"$2\"; fi\n" +
        "exit 0\n";

    public async Task<ExtensionInventory?> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await runtimes.GetStatusForUserAsync(userId, cancellationToken).ConfigureAwait(false) == RuntimeStatus.NotCreated)
        {
            return null;
        }

        var runtime = await runtimes.EnsureRuntimeAsync(userId, cancellationToken).ConfigureAwait(false);
        var spec = new RuntimeProcessSpec(
            "sh",
            ["-c", Script, "ymir-list-extensions", PiRuntimeLayout.AgentDirectory, PiRuntimeLayout.UserMcpFileName, PiExtensionConfig.MaxUserMcpBytes.ToString(CultureInfo.InvariantCulture)]);
        var process = await runtimes.StartProcessAsync(runtime.RuntimeId, spec, cancellationToken).ConfigureAwait(false);
        await using (process.ConfigureAwait(false))
        {
            process.CloseStandardInput();
            using var buffer = new MemoryStream();
            await process.StandardOutput.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return Parse(Encoding.UTF8.GetString(buffer.ToArray()));
        }
    }

    internal static ExtensionInventory Parse(string output)
    {
        var skills = new List<string>();
        var mcpServers = new List<string>();
        // 「mcp」標記行之前是 skill 清單，之後是 mcp.user.json 的內容；沒有標記表示沒有 MCP 設定。
        var mcpStart = output.StartsWith("mcp\n", StringComparison.Ordinal) ? 0
            : output.IndexOf("\nmcp\n", StringComparison.Ordinal) is var index and >= 0 ? index + 1
            : -1;
        foreach (var line in (mcpStart >= 0 ? output[..mcpStart] : output).Split('\n'))
        {
            if (line.StartsWith("skill\t", StringComparison.Ordinal) && IsDisplayableName(line["skill\t".Length..]) && skills.Count < MaxEntries)
            {
                skills.Add(line["skill\t".Length..]);
            }
        }

        if (mcpStart >= 0)
        {
            try
            {
                if (JsonNode.Parse(output[(mcpStart + "mcp\n".Length)..]) is JsonObject root && root["mcpServers"] is JsonObject servers)
                {
                    mcpServers.AddRange(servers.Select(s => s.Key).Where(IsDisplayableName).Take(MaxEntries));
                }
            }
            catch (JsonException)
            {
                // 設定檔格式錯誤時不列出（執行時也會被忽略，見 PiExtensionConfig.BuildMcpConfig）。
            }
        }

        return new ExtensionInventory(skills.Order(StringComparer.Ordinal).ToList(), mcpServers.Order(StringComparer.Ordinal).ToList());
    }

    private static bool IsDisplayableName(string name) =>
        name.Length is > 0 and <= MaxNameLength && !name.Any(char.IsControl);
}
