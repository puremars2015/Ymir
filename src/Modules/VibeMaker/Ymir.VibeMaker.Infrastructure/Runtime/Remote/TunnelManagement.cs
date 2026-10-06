using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ymir.VibeMaker.Infrastructure.Runtime.Remote;

/// <summary>
/// 由管理介面設定 Cloudflare Tunnel token（ADR-0010）。API 只轉送給主機上的 runtime host，
/// 不保存、不寫入資料庫或 log；runtime host 寫進 <c>ymir</c> 帳號的 600 檔案並重啟 cloudflared。
/// </summary>
public interface ITunnelManagement
{
    Task<TunnelState> GetStatusAsync(CancellationToken cancellationToken);

    Task<TunnelTokenOutcome> SetTokenAsync(string token, CancellationToken cancellationToken);
}

/// <param name="ManagementAvailable">這個部署能否由網頁管理 tunnel（API 經 runtime host 部署，且主機開啟 tunnel 管理）。</param>
public sealed record TunnelState(bool ManagementAvailable, bool Configured, bool Active, DateTimeOffset? UpdatedAt);

public enum TunnelTokenOutcome
{
    Success,
    Unavailable,
    Invalid,
    Failed,
}

/// <summary>沒有經由 runtime host 部署時（開發用的 Local / Podman / Docker provider）：不支援由網頁管理。</summary>
internal sealed class UnavailableTunnelManagement : ITunnelManagement
{
    public Task<TunnelState> GetStatusAsync(CancellationToken cancellationToken) => Task.FromResult(new TunnelState(false, false, false, null));

    public Task<TunnelTokenOutcome> SetTokenAsync(string token, CancellationToken cancellationToken) => Task.FromResult(TunnelTokenOutcome.Unavailable);
}

/// <summary>經 runtime host 的 <c>/v1/edge/*</c> 端點管理（ADR-0008、ADR-0010）。</summary>
internal sealed partial class RuntimeHostTunnelManagement : ITunnelManagement, IDisposable
{
    private readonly RuntimeHostConnection _connection;
    private readonly ILogger<RuntimeHostTunnelManagement> _logger;

    public RuntimeHostTunnelManagement(IOptions<RuntimeOptions> options, ILogger<RuntimeHostTunnelManagement> logger)
    {
        var remote = options.Value.Remote;
        var endpoint = RuntimeHostEndpoint.Parse(remote.Endpoint, "VibeMaker:Runtime:Remote:Endpoint", allowContainerHostAlias: true);
        RuntimeHostProtocol.EnsureTokenIsStrong(remote.Token, "VibeMaker:Runtime:Remote:Token");
        _connection = new RuntimeHostConnection(endpoint, remote.Token!);
        _logger = logger;
    }

    public async Task<TunnelState> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var status = await _connection.Http.GetFromJsonAsync<TunnelStatusMessage>(
                new Uri(RuntimeHostProtocol.TunnelPath, UriKind.Relative), RuntimeHostProtocol.JsonOptions, cancellationToken).ConfigureAwait(false);
            return status is null
                ? new TunnelState(false, false, false, null)
                : new TunnelState(status.ManagementEnabled, status.Configured, status.Active, status.UpdatedAt);
        }
        catch (HttpRequestException ex)
        {
            LogRuntimeHostUnavailable(_logger, ex);
            return new TunnelState(false, false, false, null);
        }
    }

    public async Task<TunnelTokenOutcome> SetTokenAsync(string token, CancellationToken cancellationToken)
    {
        if (!RuntimeHostProtocol.IsValidTunnelToken(token))
        {
            return TunnelTokenOutcome.Invalid;
        }

        try
        {
            using var response = await _connection.Http.PutAsJsonAsync(
                new Uri(RuntimeHostProtocol.TunnelTokenPath, UriKind.Relative), new TunnelTokenMessage(token), RuntimeHostProtocol.JsonOptions, cancellationToken).ConfigureAwait(false);
            return response.StatusCode switch
            {
                HttpStatusCode.NoContent => TunnelTokenOutcome.Success,
                HttpStatusCode.Conflict => TunnelTokenOutcome.Unavailable,
                HttpStatusCode.BadRequest => TunnelTokenOutcome.Invalid,
                _ => TunnelTokenOutcome.Failed,
            };
        }
        catch (HttpRequestException ex)
        {
            LogRuntimeHostUnavailable(_logger, ex);
            return TunnelTokenOutcome.Failed;
        }
    }

    public void Dispose() => _connection.Dispose();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Runtime host is unavailable for tunnel management")]
    private static partial void LogRuntimeHostUnavailable(ILogger logger, Exception exception);
}
