using System.Text;
using Ymir.VibeMaker.Application.Runtime;

namespace Ymir.VibeMaker.Infrastructure.PiAgent;

/// <summary>
/// Agent 啟動前在 runtime 內要做的準備工作，合併成一次 <c>sh</c> exec。
/// 每次 <c>podman exec</c>（Remote 還要經 runtime host）都有固定成本，原本建立交付目錄、寫 models.json、讀 MCP 設定、
/// 寫設定檔、每份 system prompt 各一次，第一個字出現前要連續 5～8 次 exec。
/// </summary>
internal static class PiPrepareScript
{
    /// <summary>
    /// 參數：<c>$1</c> 交付目錄（相對於工作目錄，<c>-</c> 表示不處理）、<c>$2</c> Pi agent 目錄、<c>$3</c> session 目錄（<c>-</c> 表示不建立）、
    /// <c>$4</c> 使用者 MCP 檔名（<c>-</c> 表示不處理遷移）、<c>$5</c> 要輸出多少位元組的使用者 MCP 內容（0 不輸出），其餘是要寫入的檔案路徑。
    /// stdin 每個檔案一行 base64，順序與路徑相同。
    /// 路徑一律以參數傳入而不是寫在腳本裡：Local runtime 只改寫「以 runtime 路徑開頭的參數」（<c>LocalRuntimeManager.MapRuntimePath</c>）；
    /// 內容經 stdin，不經程序參數（使用者的 MCP 設定可能含有憑證）。
    /// </summary>
    internal const string Script = """
        d=$1; a=$2; s=$3; u=$4; n=$5; shift 5
        if [ "$d" != - ]; then
          for p in deliverables .ymir .ymir/tools .ymir/tmp "$d"; do [ ! -L "$p" ] || exit 3; done
          mkdir -p -- "$d" .ymir/tools .ymir/tmp || exit 3
        fi
        if [ "$a" != - ]; then mkdir -p -- "$a" || exit 4; fi
        if [ "$s" != - ]; then mkdir -p -- "$s" || exit 4; fi
        if [ "$u" != - ]; then
          if [ ! -e "$a/$u" ] && [ -f "$a/mcp.json" ] && [ ! -e "$a/.ymir-mcp-migrated" ]; then cp "$a/mcp.json" "$a/$u"; fi
          : > "$a/.ymir-mcp-migrated"
          if [ "$n" -gt 0 ] && [ -f "$a/$u" ]; then head -c "$n" "$a/$u"; fi
        fi
        for f in "$@"; do
          IFS= read -r b || exit 2
          mkdir -p -- "${f%/*}" || exit 4
          t="$f.ymir-tmp"
          rm -f -- "$t"
          ( set -C; printf '%s' "$b" | base64 -d > "$t" ) || exit 5
          mv -f -- "$t" "$f" || exit 6
        done
        exit 0
        """;

    /// <summary>退出碼 3：交付目錄路徑上有 symlink（Agent 可能預先放置，拒絕跟隨）。</summary>
    internal const int DeliveryRejectedExitCode = 3;

    /// <summary>
    /// 組出 <c>sh -c</c> 的程序規格。<paramref name="initialize"/> 為 true 時建立 agent / session 目錄，並做使用者 MCP 的一次性遷移
    /// （必須在 Ymir 覆寫 <c>mcp.json</c> 之前，否則會把產生的檔案複製成使用者設定）。<paramref name="filePaths"/> 的內容另以
    /// <see cref="EncodeContents"/> 經 stdin 傳入。
    /// </summary>
    internal static RuntimeProcessSpec BuildSpec(
        string workingDirectory,
        string? deliveryDirectory,
        bool initialize,
        int readUserMcpBytes,
        IReadOnlyList<string> filePaths)
    {
        List<string> arguments =
        [
            "-c",
            Script,
            "ymir-prepare",
            deliveryDirectory ?? "-",
            initialize ? PiRuntimeLayout.AgentDirectory : "-",
            initialize ? PiRuntimeLayout.SessionDirectory : "-",
            initialize ? PiRuntimeLayout.UserMcpFileName : "-",
            readUserMcpBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            .. filePaths,
        ];
        return new RuntimeProcessSpec("sh", arguments, WorkingDirectory: workingDirectory);
    }

    /// <summary>每個檔案一行 base64，避免內容含換行時需要自訂分隔；空內容是一個空行。</summary>
    internal static byte[] EncodeContents(IReadOnlyList<string> contents)
    {
        var builder = new StringBuilder();
        foreach (var content in contents)
        {
            builder.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(content))).Append('\n');
        }

        return Encoding.ASCII.GetBytes(builder.ToString());
    }
}
