using System.Diagnostics;
using System.Globalization;

namespace Ymir.MaintenanceHost;

internal sealed record CliResult(int ExitCode, string Output, string Error)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>對某個 store 執行 container CLI（測試以假的實作取代）。參數一律以 argument list 傳遞，不經 shell。</summary>
internal interface IContainerCli
{
    Task<CliResult> RunAsync(MaintenanceStoreOptions store, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

/// <summary>
/// rootful store 直接執行 podman；rootless store 以 <c>runuser -u 帳號</c> 切換身分並設定該帳號的 HOME 與 XDG_RUNTIME_DIR，
/// 使用該帳號自己的 store（ADR-0025 §2：root 權限只在這個獨立服務）。
/// </summary>
internal sealed class ProcessContainerCli(string executable, ILogger<ProcessContainerCli> logger) : IContainerCli
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromMinutes(2);
    private readonly Dictionary<string, (string Uid, string Home)> _accounts = new(StringComparer.Ordinal);

    public async Task<CliResult> RunAsync(MaintenanceStoreOptions store, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // runuser 切換帳號後仍在目前目錄；/ 對所有帳號都可進入。
            WorkingDirectory = "/",
        };

        if (store.IsRootful)
        {
            start.FileName = executable;
        }
        else
        {
            var (uid, home) = await ResolveAccountAsync(store.Account!, cancellationToken).ConfigureAwait(false);
            start.FileName = "runuser";
            foreach (var argument in (string[])["-u", store.Account!, "--", "env", $"HOME={home}", $"XDG_RUNTIME_DIR=/run/user/{uid}", executable])
            {
                start.ArgumentList.Add(argument);
            }
        }

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        return await RunProcessAsync(start, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(string Uid, string Home)> ResolveAccountAsync(string account, CancellationToken cancellationToken)
    {
        lock (_accounts)
        {
            if (_accounts.TryGetValue(account, out var known))
            {
                return known;
            }
        }

        var start = new ProcessStartInfo { FileName = "getent", RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("passwd");
        start.ArgumentList.Add(account);
        var result = await RunProcessAsync(start, cancellationToken).ConfigureAwait(false);
        var fields = result.Output.Trim().Split(':');
        if (!result.Succeeded || fields.Length < 6 || !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            throw new InvalidOperationException($"Account '{account}' for a maintenance store was not found.");
        }

        lock (_accounts)
        {
            _accounts[account] = (fields[2], fields[5]);
        }

        return (fields[2], fields[5]);
    }

    private async Task<CliResult> RunProcessAsync(ProcessStartInfo start, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(s_timeout);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start container CLI.");
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // 已結束。
            }

            throw;
        }

        var result = new CliResult(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        if (!result.Succeeded)
        {
            // 原始輸出只寫 server log，不回傳給 API（SA §10）。
            logger.LogWarning("Container CLI exited with {ExitCode}: {Error}", result.ExitCode, Truncate(result.Error));
        }

        return result;
    }

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500];
}
