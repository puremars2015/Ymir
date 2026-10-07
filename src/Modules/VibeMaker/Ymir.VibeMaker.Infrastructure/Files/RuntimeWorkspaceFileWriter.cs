using System.Globalization;
using Microsoft.Extensions.Logging;
using Ymir.VibeMaker.Application.Files;
using Ymir.VibeMaker.Application.Runtime;

namespace Ymir.VibeMaker.Infrastructure.Files;

/// <summary>
/// 在使用者的 runtime 內以 <c>bash</c> 寫入檔案（使用者上傳的附件，ADR-0007、ADR-0008）。
/// <list type="bullet">
/// <item>相對路徑由伺服器產生並通過 <see cref="WorkspacePathRules.IsSafeRelativePath"/>，以位置參數傳入（不組進 shell 字串）。</item>
/// <item>目標目錄以 <c>realpath</c> 解析後必須仍在工作目錄內，避免 Agent 事先把 <c>uploads</c> 換成指向外面的 symlink。</item>
/// <item>內容經 stdin 寫到同目錄的暫存檔，大小剛好符合才 <c>mv</c> 成正式檔名；上傳中斷不會留下不完整的檔案。</item>
/// </list>
/// </summary>
internal sealed partial class RuntimeWorkspaceFileWriter(IAgentRuntimeManager runtimes, ILogger<RuntimeWorkspaceFileWriter> logger) : IWorkspaceFileWriter
{
    internal const string WriteScript = """
        base=$(realpath -e .) || exit 3
        dir=${1%/*}; name=${1##*/}
        mkdir -p -- "$dir" || exit 3
        d=$(realpath -e -- "$dir") || exit 3
        case "$d" in "$base"/*) ;; *) exit 4 ;; esac
        tmp="$d/.ymir-upload-$$-$RANDOM"
        head -c "$2" > "$tmp" || { rm -f -- "$tmp"; exit 5; }
        s=$(stat -c %s -- "$tmp") || { rm -f -- "$tmp"; exit 5; }
        [ "$s" = "$2" ] || { rm -f -- "$tmp"; exit 6; }
        mv -fT -- "$tmp" "$d/$name" || { rm -f -- "$tmp"; exit 7; }
        """;

    public async Task<bool> WriteAsync(Guid userId, string workingDirectory, string relativePath, Stream content, long size, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        // 必須有目錄（例如 uploads/x），檔案不得直接寫在工作目錄根部以外的地方。
        if (!WorkspacePathRules.IsSafeRelativePath(relativePath) || !relativePath.Contains('/', StringComparison.Ordinal) || size < 0)
        {
            return false;
        }

        var runtime = await runtimes.EnsureRuntimeAsync(userId, cancellationToken).ConfigureAwait(false);
        var process = await runtimes.StartProcessAsync(
            runtime.RuntimeId,
            new RuntimeProcessSpec("bash", ["-c", WriteScript, "ymir-write", relativePath, size.ToString(CultureInfo.InvariantCulture)], WorkingDirectory: workingDirectory),
            cancellationToken).ConfigureAwait(false);
        await using (process.ConfigureAwait(false))
        {
            try
            {
                await content.CopyToAsync(process.StandardInput, cancellationToken).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                // 腳本提早結束（例如目錄檢查失敗）時 stdin 會斷開；結果以 exit code 為準。
                LogStdinClosed(logger, ex);
            }

            process.CloseStandardInput();
            var exitCode = await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (exitCode != 0)
            {
                LogWriteFailed(logger, exitCode, process.GetStandardErrorTail());
                return false;
            }

            return true;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Writing workspace file exited with {ExitCode}: {StandardError}")]
    private static partial void LogWriteFailed(ILogger logger, int exitCode, string standardError);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Workspace file writer closed its input early")]
    private static partial void LogStdinClosed(ILogger logger, Exception exception);
}
