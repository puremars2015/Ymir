using System.Diagnostics;

namespace Ymir.UnitTests.Runtime;

/// <summary>
/// ADR-0034：image 的 <c>ymir-runtime-init</c> 與 <c>ymir-env</c>。直接以 bash 在暫存目錄執行（掃描間隔調短），
/// 驗證常駐程序會啟動、結束後重啟、刪除後停止，以及 ymir-env 只從 key 檔讀取模型 key。
/// </summary>
public sealed class RuntimeInitScriptTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ymir-init-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string ScriptPath(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "runtime", "agent", "scripts", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(name);
    }

    private static ProcessStartInfo Bash(string script, params string[] args)
    {
        var info = new ProcessStartInfo("bash") { RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(ScriptPath(script));
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        return info;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "condition was not met in time");
            await Task.Delay(200, Ct);
        }
    }

    private int Lines(string file) => File.Exists(Path.Combine(_root, file)) ? File.ReadAllLines(Path.Combine(_root, file)).Length : 0;

    [Fact]
    public async Task Init_RunsAutostartScripts_RestartsThem_AndStopsRemovedOnes()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the runtime image is Linux only");
        var autostart = Directory.CreateDirectory(Path.Combine(_root, "autostart")).FullName;
        // 每次啟動記一行後立即結束：會被重啟（最短退避 5 秒）。
        await File.WriteAllTextAsync(Path.Combine(autostart, "once.sh"), $"echo started >> '{_root}/once.out'\n", Ct);
        await File.WriteAllTextAsync(Path.Combine(autostart, "beat.sh"), $"while true; do echo x >> '{_root}/beat.out'; sleep 0.2; done\n", Ct);
        await File.WriteAllTextAsync(Path.Combine(autostart, "bad name.sh"), $"echo x >> '{_root}/bad.out'\n", Ct);

        var info = Bash("ymir-runtime-init");
        info.Environment["YMIR_BACKGROUND_ROOT"] = _root;
        info.Environment["YMIR_AUTOSTART_SCAN_SECONDS"] = "1";
        using var init = Process.Start(info)!;
        try
        {
            await WaitUntilAsync(() => Lines("once.out") >= 2, TimeSpan.FromSeconds(20));
            Assert.True(Lines("beat.out") > 0);
            Assert.Contains("ymir: exited with 0", await File.ReadAllTextAsync(Path.Combine(_root, "logs", "once.log"), Ct), StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(_root, "bad.out"))); // 名稱不合法的檔案不執行

            File.Delete(Path.Combine(autostart, "beat.sh"));
            await Task.Delay(2500, Ct);
            var stopped = Lines("beat.out");
            await Task.Delay(1000, Ct);
            Assert.Equal(stopped, Lines("beat.out"));
        }
        finally
        {
            Process.Start("kill", ["-TERM", init.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)])!.WaitForExit();
            await init.WaitForExitAsync(Ct);
        }

        Assert.Equal(0, init.ExitCode);
    }

    [Fact]
    public async Task Env_ExportsTheKeyFromTheKeyFile_OrFailsWhenMissing()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the runtime image is Linux only");
        var keyFile = Path.Combine(_root, "model-key");

        var missing = Bash("ymir-env", "sh", "-c", "echo should-not-run");
        missing.Environment["YMIR_MODEL_KEY_FILE"] = keyFile;
        using (var process = Process.Start(missing)!)
        {
            await process.WaitForExitAsync(Ct);
            Assert.Equal(78, process.ExitCode);
            Assert.DoesNotContain("should-not-run", await process.StandardOutput.ReadToEndAsync(Ct), StringComparison.Ordinal);
        }

        await File.WriteAllTextAsync(keyFile, "sk-background-test", Ct);
        var present = Bash("ymir-env", "sh", "-c", "printf %s \"$LITELLM_API_KEY\"");
        present.Environment["YMIR_MODEL_KEY_FILE"] = keyFile;
        using (var process = Process.Start(present)!)
        {
            var output = await process.StandardOutput.ReadToEndAsync(Ct);
            await process.WaitForExitAsync(Ct);
            Assert.Equal(0, process.ExitCode);
            Assert.Equal("sk-background-test", output);
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
