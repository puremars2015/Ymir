using System.Diagnostics;
using System.Text;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Infrastructure.PiAgent;

namespace Ymir.UnitTests.PiAgent;

/// <summary>
/// Agent 啟動前的準備工作合併成一次 runtime exec（<see cref="PiPrepareScript"/>）。
/// 這裡用真正的 <c>sh</c> 執行腳本，確認合併後的行為與原本分開的步驟一致（交付目錄、symlink 拒絕、MCP 遷移、原子寫檔）。
/// </summary>
public sealed class PiPrepareScriptTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _root = Directory.CreateTempSubdirectory("ymir-prepare-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Work => Path.Combine(_root, "work");
    private string Agent => Path.Combine(_root, "agent");
    private string Sessions => Path.Combine(_root, "sessions");

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(string delivery, string agent, string sessions, string userMcp, int readBytes, IReadOnlyList<(string Path, string Content)> files)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "uses sh and base64");
        Directory.CreateDirectory(Work);
        var info = new ProcessStartInfo("sh") { WorkingDirectory = Work, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        string[] arguments = ["-c", PiPrepareScript.Script, "ymir-prepare", delivery, agent, sessions, userMcp, readBytes.ToString(System.Globalization.CultureInfo.InvariantCulture), .. files.Select(f => f.Path)];
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync(Ct);
        var error = process.StandardError.ReadToEndAsync(Ct);
        try
        {
            await process.StandardInput.BaseStream.WriteAsync(PiPrepareScript.EncodeContents([.. files.Select(f => f.Content)]), Ct);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // 腳本提早結束（拒絕交付目錄）時不再讀 stdin。
        }

        await process.WaitForExitAsync(Ct);
        return (process.ExitCode, await output, await error);
    }

    [Fact]
    public async Task WritesEveryFileWithExactContent_IncludingNewlinesQuotesUnicodeAndEmpty()
    {
        var tricky = "line1\nline \"2\" 'x' $HOME `id` \\n 繁體中文 🚀\n\n  trailing  \n";
        var files = new List<(string, string)>
        {
            (Path.Combine(Agent, "settings.json"), "{}"),
            (Path.Combine(_root, "deep", "nested", "SKILL.md"), tricky),
            (Path.Combine(_root, "prompts", "a-0.md"), string.Empty),
        };

        var result = await RunAsync("deliverables/abc", Agent, Sessions, PiRuntimeLayout.UserMcpFileName, 0, files);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Output);
        foreach (var (path, content) in files)
        {
            Assert.Equal(content, await File.ReadAllTextAsync(path, Ct));
        }

        Assert.True(Directory.Exists(Path.Combine(Work, "deliverables", "abc")));
        Assert.True(Directory.Exists(Path.Combine(Work, ".ymir", "tools")));
        Assert.True(Directory.Exists(Path.Combine(Work, ".ymir", "tmp")));
        Assert.True(Directory.Exists(Sessions));
        Assert.Empty(Directory.GetFiles(_root, "*.ymir-tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task LargeContent_IsWrittenIntactWhileOutputIsRead()
    {
        // 超過 pipe 緩衝：stdin 與 stdout 必須能同時進行。
        Directory.CreateDirectory(Agent);
        await File.WriteAllTextAsync(Path.Combine(Agent, PiRuntimeLayout.UserMcpFileName), new string('m', 200_000), Ct);
        var big = new string('p', 500_000);

        var result = await RunAsync("-", Agent, Sessions, PiRuntimeLayout.UserMcpFileName, 262_145, [(Path.Combine(_root, "prompt.md"), big)]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(200_000, result.Output.Length);
        Assert.Equal(big, await File.ReadAllTextAsync(Path.Combine(_root, "prompt.md"), Ct));
    }

    [Fact]
    public async Task SymlinkOnTheDeliveryPath_IsRejectedBeforeAnythingIsWritten()
    {
        Directory.CreateDirectory(Work);
        var outside = Directory.CreateDirectory(Path.Combine(_root, "outside")).FullName;
        Directory.CreateSymbolicLink(Path.Combine(Work, "deliverables"), outside);

        var result = await RunAsync("deliverables/abc", Agent, Sessions, PiRuntimeLayout.UserMcpFileName, 0, [(Path.Combine(Agent, "settings.json"), "{}")]);

        Assert.Equal(PiPrepareScript.DeliveryRejectedExitCode, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(Agent, "settings.json")));
        Assert.Empty(Directory.GetFileSystemEntries(outside));
    }

    [Fact]
    public async Task PreplacedSymlinks_AreReplacedNotFollowed()
    {
        Directory.CreateDirectory(Agent);
        var victim = Path.Combine(_root, "victim.txt");
        await File.WriteAllTextAsync(victim, "original", Ct);
        var target = Path.Combine(Agent, "settings.json");
        File.CreateSymbolicLink(target, victim);
        File.CreateSymbolicLink(Path.Combine(Agent, "trust.json.ymir-tmp"), victim);

        var result = await RunAsync("-", Agent, "-", "-", 0, [(target, "new"), (Path.Combine(Agent, "trust.json"), "trust")]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("original", await File.ReadAllTextAsync(victim, Ct));
        Assert.Equal("new", await File.ReadAllTextAsync(target, Ct));
        Assert.Null(new FileInfo(target).LinkTarget);
        Assert.Equal("trust", await File.ReadAllTextAsync(Path.Combine(Agent, "trust.json"), Ct));
    }

    [Fact]
    public async Task UserMcp_IsMigratedOnceBeforeTheGeneratedFileIsWritten_AndReadBack()
    {
        Directory.CreateDirectory(Agent);
        var mcp = Path.Combine(Agent, "mcp.json");
        await File.WriteAllTextAsync(mcp, """{"mcpServers":{"mine":{"url":"https://x"}}}""", Ct);

        // 第一次：遷移 Agent 先前寫的 mcp.json，再由 Ymir 覆寫產生的檔案。
        var first = await RunAsync("-", Agent, Sessions, PiRuntimeLayout.UserMcpFileName, 1024, [(mcp, """{"mcpServers":{}}""")]);
        Assert.Equal(0, first.ExitCode);
        Assert.Equal("""{"mcpServers":{"mine":{"url":"https://x"}}}""", first.Output);
        Assert.Equal("""{"mcpServers":{}}""", await File.ReadAllTextAsync(mcp, Ct));

        // 第二次：已有標記，產生的 mcp.json 不會被複製成使用者設定。
        var second = await RunAsync("-", Agent, Sessions, PiRuntimeLayout.UserMcpFileName, 1024, []);
        Assert.Equal("""{"mcpServers":{"mine":{"url":"https://x"}}}""", second.Output);
    }

    [Fact]
    public async Task ReadIsCappedAtTheRequestedBytes()
    {
        Directory.CreateDirectory(Agent);
        await File.WriteAllTextAsync(Path.Combine(Agent, PiRuntimeLayout.UserMcpFileName), "0123456789", Ct);

        var result = await RunAsync("-", Agent, Sessions, PiRuntimeLayout.UserMcpFileName, 4, []);

        Assert.Equal("0123", result.Output);
    }

    [Fact]
    public void BuildSpec_PassesPathsAsArgumentsAndKeepsContentOutOfThem()
    {
        var secret = "Bearer personal-fixture-secret";
        var spec = PiPrepareScript.BuildSpec(RuntimePaths.Workspace, "deliverables/x", initialize: true, 4, [PiRuntimeLayout.McpConfigPath]);

        Assert.Equal("sh", spec.Executable);
        Assert.Equal(["-c", PiPrepareScript.Script, "ymir-prepare", "deliverables/x", PiRuntimeLayout.AgentDirectory, PiRuntimeLayout.SessionDirectory, PiRuntimeLayout.UserMcpFileName, "4", PiRuntimeLayout.McpConfigPath], spec.Arguments);
        Assert.DoesNotContain(Encoding.ASCII.GetString(PiPrepareScript.EncodeContents([secret])), spec.Arguments);

        var followUp = PiPrepareScript.BuildSpec(RuntimePaths.Workspace, null, initialize: false, 0, []);
        Assert.Equal(["-", "-", "-", "-", "0"], followUp.Arguments.Skip(3));
    }
}
