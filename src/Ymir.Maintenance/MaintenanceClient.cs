using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Ymir.Maintenance;

/// <summary>API 端連到主機維護服務（ADR-0025 §2）。查詢失敗回傳 null（呼叫端顯示為「無法取得」，不當作零）。</summary>
public interface IMaintenanceClient
{
    /// <summary>這個部署是否設定了維護服務；Windows Docker / Local 開發環境為 false（管理介面顯示不支援）。</summary>
    bool IsConfigured { get; }

    Task<ResourceSnapshot?> GetResourcesAsync(CancellationToken cancellationToken);

    Task<bool> SetPolicyAsync(ReclamationPolicy policy, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReclaimRun>?> GetRunsAsync(long afterId, CancellationToken cancellationToken);

    Task<DryRunResult?> DryRunAsync(CancellationToken cancellationToken);
}

/// <summary>設定區段 <c>Ymir:Maintenance</c>。Token 只放部署 secret（<c>deploy/api/.env</c>），不得進版控。</summary>
public sealed class MaintenanceClientOptions
{
    public const string SectionName = "Ymir:Maintenance";

    /// <summary><c>unix:/run/ymir-maintenance/maintenance.sock</c>；未設定時停用（管理介面顯示不支援）。</summary>
    public string? Endpoint { get; set; }

    public string? Token { get; set; }
}

public sealed class UnconfiguredMaintenanceClient : IMaintenanceClient
{
    public bool IsConfigured => false;

    public Task<ResourceSnapshot?> GetResourcesAsync(CancellationToken cancellationToken) => Task.FromResult<ResourceSnapshot?>(null);

    public Task<bool> SetPolicyAsync(ReclamationPolicy policy, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<IReadOnlyList<ReclaimRun>?> GetRunsAsync(long afterId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ReclaimRun>?>(null);

    public Task<DryRunResult?> DryRunAsync(CancellationToken cancellationToken) => Task.FromResult<DryRunResult?>(null);
}

public sealed partial class MaintenanceClient : IMaintenanceClient, IDisposable
{
    private static readonly Uri s_unixBaseUri = new("http://localhost/");

    private readonly SocketsHttpHandler _handler;
    private readonly HttpClient _http;
    private readonly ILogger<MaintenanceClient> _logger;

    public MaintenanceClient(MaintenanceClientOptions options, ILogger<MaintenanceClient> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        var endpoint = MaintenanceEndpoint.Parse(options.Endpoint, "Ymir:Maintenance:Endpoint");
        MaintenanceProtocol.EnsureTokenIsStrong(options.Token, "Ymir:Maintenance:Token");
        _logger = logger;
        _handler = new SocketsHttpHandler { PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1) };
        if (endpoint.SocketPath is { } socketPath)
        {
            _handler.ConnectCallback = async (_, cancellationToken) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            };
        }

        // dry run 需要盤點所有 store；逾時放寬到 2 分鐘。
        _http = new HttpClient(_handler, disposeHandler: false) { BaseAddress = endpoint.HttpUri ?? s_unixBaseUri, Timeout = TimeSpan.FromMinutes(2) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(MaintenanceProtocol.AuthorizationScheme, options.Token);
    }

    public bool IsConfigured => true;

    public Task<ResourceSnapshot?> GetResourcesAsync(CancellationToken cancellationToken) =>
        GetAsync<ResourceSnapshot>(MaintenanceProtocol.ResourcesPath, cancellationToken);

    public Task<IReadOnlyList<ReclaimRun>?> GetRunsAsync(long afterId, CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<ReclaimRun>>(MaintenanceProtocol.RunsAfterPath(afterId), cancellationToken);

    public async Task<bool> SetPolicyAsync(ReclamationPolicy policy, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.PutAsJsonAsync(new Uri(MaintenanceProtocol.PolicyPath, UriKind.Relative), policy, MaintenanceProtocol.JsonOptions, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogUnavailable(_logger, ex);
            return false;
        }
    }

    public async Task<DryRunResult?> DryRunAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.PostAsync(new Uri(MaintenanceProtocol.DryRunPath, UriKind.Relative), content: null, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<DryRunResult>(MaintenanceProtocol.JsonOptions, cancellationToken).ConfigureAwait(false)
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogUnavailable(_logger, ex);
            return null;
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await _http.GetFromJsonAsync<T>(new Uri(path, UriKind.Relative), MaintenanceProtocol.JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogUnavailable(_logger, ex);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Maintenance host is unavailable")]
    private static partial void LogUnavailable(ILogger logger, Exception exception);
}
