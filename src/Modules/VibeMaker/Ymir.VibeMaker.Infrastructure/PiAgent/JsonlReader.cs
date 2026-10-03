using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;

namespace Ymir.VibeMaker.Infrastructure.PiAgent;

/// <summary>
/// Pi RPC 的 JSONL framing：只以 LF (0x0A) 切 record，並去除結尾的 CR。
/// 不可使用一般的 line reader，因為 JSON 字串內合法出現的 U+2028/U+2029 不是 record 邊界（Pi rpc.md「Framing」）。
/// </summary>
internal static class JsonlReader
{
    public static async IAsyncEnumerable<string> ReadRecordsAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var reader = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            while (true)
            {
                var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                var buffer = result.Buffer;

                while (TryReadRecord(ref buffer, out var record))
                {
                    if (record.Length > 0)
                    {
                        yield return record;
                    }
                }

                if (result.IsCompleted)
                {
                    if (buffer.Length > 0)
                    {
                        var tail = Decode(buffer);
                        if (tail.Length > 0)
                        {
                            yield return tail;
                        }
                    }

                    reader.AdvanceTo(buffer.End);
                    yield break;
                }

                reader.AdvanceTo(buffer.Start, buffer.End);
            }
        }
        finally
        {
            await reader.CompleteAsync().ConfigureAwait(false);
        }
    }

    private static bool TryReadRecord(ref ReadOnlySequence<byte> buffer, out string record)
    {
        var position = buffer.PositionOf((byte)'\n');
        if (position is null)
        {
            record = string.Empty;
            return false;
        }

        record = Decode(buffer.Slice(0, position.Value));
        buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
        return true;
    }

    private static string Decode(ReadOnlySequence<byte> line)
    {
        var text = Encoding.UTF8.GetString(line);
        return text.EndsWith('\r') ? text[..^1] : text;
    }
}
