using System.Text;
using Ymir.VibeMaker.Application.Files;
using Ymir.VibeMaker.Infrastructure.Files;

namespace Ymir.UnitTests.Files;

/// <summary>對話檔案下載的路徑規則與 runtime 輸出解析（SA §12）。</summary>
public class WorkspaceFileRulesTests
{
    [Theory]
    [InlineData("calculator.html")]
    [InlineData("src/中文/資料 1.bin")]
    [InlineData("a..b/c")]
    public void SafeRelativePaths_AreAccepted(string path) => Assert.True(WorkspacePathRules.IsSafeRelativePath(path));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/etc/passwd")]
    [InlineData("../secret")]
    [InlineData("a/../../b")]
    [InlineData("a/./b")]
    [InlineData("a//b")]
    [InlineData("a\\b")]
    [InlineData(".env")]
    [InlineData("src/.git/config")]
    [InlineData("node_modules/x.js")]
    [InlineData("line\nbreak")]
    public void UnsafePaths_AreRejected(string? path) => Assert.False(WorkspacePathRules.IsSafeRelativePath(path));

    [Fact]
    public void LongPaths_AreRejected() =>
        Assert.False(WorkspacePathRules.IsSafeRelativePath(new string('a', WorkspacePathRules.MaxPathLength + 1)));

    [Fact]
    public void FileName_IsTheLastSegment()
    {
        Assert.Equal("c.txt", WorkspacePathRules.FileName("a/b/c.txt"));
        Assert.Equal("c.txt", WorkspacePathRules.FileName("c.txt"));
    }

    [Fact]
    public void ParseList_ReadsNulSeparatedRecords_AndDropsUnsafeOnes()
    {
        var output = Encoding.UTF8.GetBytes("index.html\0" + "120\0" + "1791270000.5\0" + "../x\0" + "1\0" + "1\0" + "src/中文.txt\0" + "3\0" + "1791270001.0\0");

        var entries = RuntimeWorkspaceFileReader.ParseList(output, 10);

        Assert.Equal(["index.html", "src/中文.txt"], entries.Select(e => e.Path));
        Assert.Equal(120, entries[0].Size);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_791_270_000_500), entries[0].ModifiedAt);
    }

    [Fact]
    public void ParseList_StopsAtTheLimit()
    {
        var output = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 5).Select(i => $"f{i}\0" + "1\0" + "1\0")));

        Assert.Equal(3, RuntimeWorkspaceFileReader.ParseList(output, 3).Count);
    }

    [Fact]
    public async Task BoundedStream_ReadsExactlyItsLength_AndDrainsTheRest()
    {
        var inner = new MemoryStream(Encoding.ASCII.GetBytes("helloWORLD"));
        var first = new BoundedReadStream(inner, 5);
        var buffer = new byte[3];

        Assert.Equal(3, await first.ReadAsync(buffer, TestContext.Current.CancellationToken));
        await first.DrainAsync(TestContext.Current.CancellationToken);
        var rest = await new StreamReader(new BoundedReadStream(inner, 5)).ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Equal("WORLD", rest);
    }
}
