using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Ymir.VibeMaker.Infrastructure.Runtime.Remote;

namespace Ymir.RuntimeHost;

/// <summary>由網頁管理 Cloudflare Tunnel 的方式（ADR-0010）。</summary>
public enum TunnelManagementMode
{
    /// <summary>不開放（預設，例如 Windows 開發機）：網頁只顯示手動設定步驟。</summary>
    Disabled,

    /// <summary>cloudflared 是 runtime host 帳號（<c>ymir</c>）的 systemd user service（rootless Podman Quadlet）。</summary>
    SystemdUser,
}

/// <summary>設定區段 <c>RuntimeHost:Tunnel</c>。</summary>
public sealed class TunnelOptions
{
    public TunnelManagementMode Mode { get; set; } = TunnelManagementMode.Disabled;

    /// <summary>cloudflared 讀取的 env 檔；預設 <c>~/.config/ymir/cloudflared.env</c>（runtime host 帳號的家目錄）。</summary>
    public string? EnvFile { get; set; }

    public string Unit { get; set; } = "ymir-cloudflared.service";
}

/// <summary>重啟 / 查詢 cloudflared 服務（測試以 fake 取代）。</summary>
public interface ITunnelServiceController
{
    Task RestartAsync(CancellationToken cancellationToken);

    Task<bool> IsActiveAsync(CancellationToken cancellationToken);
}

/// <summary>
/// <c>systemctl --user</c>：以參數清單執行（不經 shell），unit 名稱在啟動時驗證。
/// runtime host 本身就是 <c>ymir</c> 帳號，不需要 sudo 或任何提升權限（ADR-0008 最小權限）。
/// </summary>
internal sealed partial class SystemdTunnelServiceController(string unit) : ITunnelServiceController
{
    public string Unit { get; } = IsValidUnit(unit) ? unit : throw new InvalidOperationException("RuntimeHost:Tunnel:Unit must be a systemd service name such as ymir-cloudflared.service.");

    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        var (exitCode, error) = await RunAsync(["--user", "restart", Unit], cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"systemctl restart {Unit} failed with exit code {exitCode}: {error}");
        }
    }

    public async Task<bool> IsActiveAsync(CancellationToken cancellationToken) =>
        (await RunAsync(["--user", "is-active", "--quiet", Unit], cancellationToken).ConfigureAwait(false)).ExitCode == 0;

    internal static bool IsValidUnit(string? unit) => unit is not null && UnitPattern().IsMatch(unit);

    private static async Task<(int ExitCode, string Error)> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("systemctl") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start systemctl.");
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (process.ExitCode, (await error.ConfigureAwait(false)).Trim());
    }

    [GeneratedRegex(@"^[A-Za-z0-9@._-]{1,200}\.service\z", RegexOptions.CultureInvariant)]
    private static partial Regex UnitPattern();
}

/// <summary>
/// Tunnel token 的保存與套用（ADR-0010）：寫進只有 runtime host 帳號讀得到的 env 檔（600），再重啟 cloudflared。
/// token 不寫 log、不回傳；API 與資料庫都不保存。
/// </summary>
internal sealed partial class TunnelManager(TunnelOptions options, ITunnelServiceController controller, ILogger<TunnelManager> logger)
{
    public const string EnvVariable = "TUNNEL_TOKEN";

    public bool Enabled => options.Mode != TunnelManagementMode.Disabled;

    public string EnvFile { get; } = ResolveEnvFile(options.EnvFile);

    public async Task<TunnelStatusMessage> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return new TunnelStatusMessage(false, false, false, null);
        }

        var file = new FileInfo(EnvFile);
        var active = await controller.IsActiveAsync(cancellationToken).ConfigureAwait(false);
        return new TunnelStatusMessage(true, file.Exists, active, file.Exists ? new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero) : null);
    }

    public async Task<TunnelSetResult> SetTokenAsync(string? token, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return TunnelSetResult.Disabled;
        }

        if (!RuntimeHostProtocol.IsValidTunnelToken(token))
        {
            return TunnelSetResult.Invalid;
        }

        await WriteEnvFileAsync(EnvFile, token!, cancellationToken).ConfigureAwait(false);
        try
        {
            await controller.RestartAsync(cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // 重啟失敗只回摘要給 API；細節寫 log（不含 token）。
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRestartFailed(logger, ex);
            return TunnelSetResult.RestartFailed;
        }

        LogTokenUpdated(logger);
        return TunnelSetResult.Success;
    }

    /// <summary>先寫暫存檔（建立時就是 600），再以 rename 取代：cloudflared 不會讀到寫一半的檔案，權限也不會有空窗。</summary>
    internal static async Task WriteEnvFileAsync(string path, string token, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        var streamOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Options = FileOptions.Asynchronous };
        if (!OperatingSystem.IsWindows())
        {
            streamOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        try
        {
            var stream = new FileStream(temp, streamOptions);
            await using (stream.ConfigureAwait(false))
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"{EnvVariable}={token}\n"), cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    private static string ResolveEnvFile(string? configured) =>
        string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "ymir", "cloudflared.env")
            : Path.GetFullPath(configured);

    [LoggerMessage(Level = LogLevel.Error, Message = "Restarting the Cloudflare tunnel service failed")]
    private static partial void LogRestartFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cloudflare tunnel token updated and service restarted")]
    private static partial void LogTokenUpdated(ILogger logger);
}

public enum TunnelSetResult
{
    Success,
    Disabled,
    Invalid,
    RestartFailed,
}
