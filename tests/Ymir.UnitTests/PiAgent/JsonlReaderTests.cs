using System.IO.Pipelines;
using System.Text;
using Ymir.VibeMaker.Infrastructure.PiAgent;

namespace Ymir.UnitTests.PiAgent;

public class JsonlReaderTests
{
    private static async Task<List<string>> ReadAllAsync(Stream stream)
    {
        var records = new List<string>();
        await foreach (var record in JsonlReader.ReadRecordsAsync(stream, TestContext.Current.CancellationToken))
        {
            records.Add(record);
        }

        return records;
    }

    [Fact]
    public async Task SplitsOnLfOnly_KeepingUnicodeLineSeparatorsInsideRecords()
    {
        var input = "{\"a\":\"x\u2028y\u2029z\"}\n{\"b\":1}\r\n\n{\"c\":2}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(input));

        var records = await ReadAllAsync(stream);

        Assert.Equal(["{\"a\":\"x\u2028y\u2029z\"}", "{\"b\":1}", "{\"c\":2}"], records);
    }

    [Fact]
    public async Task HandlesRecordsSplitAcrossWrites_IncludingMultiByteCharacters()
    {
        var pipe = new Pipe();
        var bytes = Encoding.UTF8.GetBytes("{\"text\":\"正在建立\"}\n{\"done\":true}\n");
        var ct = TestContext.Current.CancellationToken;

        var readTask = ReadAllAsync(pipe.Reader.AsStream());
        foreach (var b in bytes)
        {
            await pipe.Writer.WriteAsync(new[] { b }, ct);
        }

        await pipe.Writer.CompleteAsync();

        Assert.Equal(["{\"text\":\"正在建立\"}", "{\"done\":true}"], await readTask);
    }
}
