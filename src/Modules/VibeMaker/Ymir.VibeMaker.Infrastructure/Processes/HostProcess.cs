using System.Diagnostics;
using System.Text;
using Ymir.VibeMaker.Application.Runtime;

namespace Ymir.VibeMaker.Infrastructure.Processes;

/// <summary>Host 上的子程序（直接執行，或執行 <c>podman exec -i</c>），stdin/stdout 以 byte stream 提供。</summary>
internal sealed class HostProcess : IRuntimeProcess
{
    private const int StderrTailLimit = 8192;

    private readonly Process _process;
    private readonly StringBuilder _stderrTail = new();
    private readonly Lock _stderrLock = new();

    private HostProcess(Process process)
    {
        _process = process;
    }

    public Stream StandardInput => _process.StandardInput.BaseStream;

    public Stream StandardOutput => _process.StandardOutput.BaseStream;

    public bool HasExited => _process.HasExited;

    public static HostProcess Start(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environment)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            WorkingDirectory = workingDirectory ?? string.Empty,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var hostProcess = new HostProcess(process);
        process.ErrorDataReceived += (_, e) => hostProcess.AppendStderr(e.Data);
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process '{fileName}'.");
        }

        process.BeginErrorReadLine();
        return hostProcess;
    }

    public void CloseStandardInput()
    {
        try
        {
            _process.StandardInput.Close();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // stdin 已關閉，或程序已結束。
        }
    }

    public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
    {
        await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return _process.ExitCode;
    }

    public void Kill()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // 程序已結束。
        }
    }

    public string GetStandardErrorTail()
    {
        lock (_stderrLock)
        {
            return _stderrTail.ToString();
        }
    }

    public async ValueTask DisposeAsync()
    {
        CloseStandardInput();

        if (!_process.HasExited)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Kill();
            }
        }

        _process.Dispose();
    }

    private void AppendStderr(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_stderrLock)
        {
            _stderrTail.AppendLine(line);
            if (_stderrTail.Length > StderrTailLimit)
            {
                _stderrTail.Remove(0, _stderrTail.Length - StderrTailLimit);
            }
        }
    }
}
