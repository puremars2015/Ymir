using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Ymir.VibeMaker.Application.Files;
using Ymir.VibeMaker.Application.Runtime;

namespace Ymir.VibeMaker.Infrastructure.Files;

/// <summary>
/// 在使用者的 runtime 內執行 <c>find</c> / <c>bash</c> 讀取工作目錄的檔案（ADR-0007、ADR-0008）。
/// <list type="bullet">
/// <item>路徑一律經 stdin（NUL 分隔）傳入，不會被組進命令列或 shell 字串。</item>
/// <item>每個路徑在 runtime 內以 <c>realpath</c> 解析，必須仍在工作目錄內、且本身不是 symlink、是一般檔案，否則略過。</item>
/// <item>輸出格式：每個檔案先一行十進位大小（<c>-1</c> 表示略過），接著剛好該大小的內容（檔案在讀取中途變短時補 0，避免串流錯位）。</item>
/// </list>
/// </summary>
internal sealed partial class RuntimeWorkspaceFileReader(IAgentRuntimeManager runtimes, ILogger<RuntimeWorkspaceFileReader> logger) : IWorkspaceFileReader
{
    /// <summary>
    /// <c>-mindepth 1</c> 讓根目錄 <c>.</c> 不被 <c>-name '.*'</c> 剪掉；隱藏檔與 node_modules 整個略過；
    /// <c>-type f</c> 不跟隨 symlink。輸出以 NUL 分隔：路徑、大小、修改時間（epoch 秒）。
    /// </summary>
    internal const string ListScript =
        "find . -mindepth 1 \\( -name '.*' -o -name node_modules \\) -prune -o -type f -printf '%P\\0%s\\0%T@\\0' 2>/dev/null | head -z -n \"$1\"";

    internal const string ReadScript = """
        base=$(realpath -e .) || exit 3
        while IFS= read -r -d '' p; do
          f=$(realpath -e -- "$p" 2>/dev/null) || { printf -- '-1\n'; continue; }
          case "$f" in "$base"/*) ;; *) printf -- '-1\n'; continue ;; esac
          if [ -L "$p" ] || [ ! -f "$f" ]; then printf -- '-1\n'; continue; fi
          s=$(stat -c %s -- "$f") || { printf -- '-1\n'; continue; }
          printf '%s\n' "$s"
          { head -c "$s" -- "$f"; head -c "$s" /dev/zero; } | head -c "$s"
        done
        """;

    public async Task<IReadOnlyList<WorkspaceFileEntry>> ListAsync(Guid userId, string workingDirectory, int limit, CancellationToken cancellationToken)
    {
        var runtime = await runtimes.EnsureRuntimeAsync(userId, cancellationToken).ConfigureAwait(false);
        var spec = new RuntimeProcessSpec(
            "sh",
            ["-c", ListScript, "ymir-list", ((limit + 1) * 3).ToString(CultureInfo.InvariantCulture)],
            WorkingDirectory: workingDirectory);
        var process = await runtimes.StartProcessAsync(runtime.RuntimeId, spec, cancellationToken).ConfigureAwait(false);
        await using (process.ConfigureAwait(false))
        {
            process.CloseStandardInput();
            using var buffer = new MemoryStream();
            await process.StandardOutput.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return ParseList(buffer.ToArray(), limit + 1);
        }
    }

    public async Task ReadAsync(
        Guid userId,
        string workingDirectory,
        IReadOnlyList<string> relativePaths,
        Func<WorkspaceFileContent, CancellationToken, Task> onFile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(relativePaths);
        ArgumentNullException.ThrowIfNull(onFile);
        if (relativePaths.Count == 0)
        {
            return;
        }

        var runtime = await runtimes.EnsureRuntimeAsync(userId, cancellationToken).ConfigureAwait(false);
        var process = await runtimes.StartProcessAsync(
            runtime.RuntimeId,
            new RuntimeProcessSpec("bash", ["-c", ReadScript, "ymir-read"], WorkingDirectory: workingDirectory),
            cancellationToken).ConfigureAwait(false);
        await using (process.ConfigureAwait(false))
        {
            // 同時寫 stdin、讀 stdout：路徑清單很長時，單向等待會讓雙方都卡在 pipe buffer。
            var writer = WritePathsAsync(process, relativePaths, cancellationToken);
            var output = process.StandardOutput;
            foreach (var path in relativePaths)
            {
                var header = await ReadLineAsync(output, cancellationToken).ConfigureAwait(false);
                if (header is null)
                {
                    break;
                }

                if (!long.TryParse(header, NumberStyles.None, CultureInfo.InvariantCulture, out var size))
                {
                    continue; // "-1"：不存在或不允許
                }

                var content = new BoundedReadStream(output, size);
                await onFile(new WorkspaceFileContent(path, size, content), cancellationToken).ConfigureAwait(false);
                await content.DrainAsync(cancellationToken).ConfigureAwait(false);
            }

            await writer.ConfigureAwait(false);
            var exitCode = await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (exitCode != 0)
            {
                LogReadFailed(logger, exitCode, process.GetStandardErrorTail());
            }
        }
    }

    internal static IReadOnlyList<WorkspaceFileEntry> ParseList(byte[] output, int limit)
    {
        var fields = Encoding.UTF8.GetString(output).Split('\0');
        var entries = new List<WorkspaceFileEntry>();
        for (var i = 0; i + 2 < fields.Length && entries.Count < limit; i += 3)
        {
            var path = fields[i];
            if (!WorkspacePathRules.IsSafeRelativePath(path)
                || !long.TryParse(fields[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var size)
                || !double.TryParse(fields[i + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out var epoch))
            {
                continue;
            }

            entries.Add(new WorkspaceFileEntry(path, size, DateTimeOffset.FromUnixTimeMilliseconds((long)(epoch * 1000))));
        }

        return entries;
    }

    private static async Task WritePathsAsync(IRuntimeProcess process, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        foreach (var path in paths)
        {
            await process.StandardInput.WriteAsync(Encoding.UTF8.GetBytes(path + "\0"), cancellationToken).ConfigureAwait(false);
        }

        await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        process.CloseStandardInput();
    }

    /// <summary>逐位元組讀一行（header 很短；內容部分由 <see cref="BoundedReadStream"/> 直接讀，不經過緩衝，避免多讀）。</summary>
    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>(24);
        var one = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return bytes.Count == 0 ? null : Encoding.ASCII.GetString([.. bytes]);
            }

            if (one[0] == (byte)'\n')
            {
                return Encoding.ASCII.GetString([.. bytes]);
            }

            if (bytes.Count > 24)
            {
                return null; // 格式錯誤：header 不應該這麼長
            }

            bytes.Add(one[0]);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading workspace files exited with {ExitCode}: {StandardError}")]
    private static partial void LogReadFailed(ILogger logger, int exitCode, string standardError);
}

/// <summary>只讀到指定長度的唯讀串流（共用底層 stdout，不關閉它）。</summary>
internal sealed class BoundedReadStream(Stream inner, long length) : Stream
{
    private readonly long _length = length;
    private long _remaining = length;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => _length;

    public override long Position
    {
        get => _length - _remaining;
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_remaining <= 0)
        {
            return 0;
        }

        var toRead = (int)Math.Min(buffer.Length, _remaining);
        var read = await inner.ReadAsync(buffer[..toRead], cancellationToken).ConfigureAwait(false);
        if (read == 0)
        {
            throw new EndOfStreamException("The workspace file stream ended early.");
        }

        _remaining -= read;
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    public async Task DrainAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        while (await ReadAsync(buffer, cancellationToken).ConfigureAwait(false) > 0)
        {
        }
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
