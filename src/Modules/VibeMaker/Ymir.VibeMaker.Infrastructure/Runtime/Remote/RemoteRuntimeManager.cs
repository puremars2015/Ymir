using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ymir.VibeMaker.Application.Runtime;

namespace Ymir.VibeMaker.Infrastructure.Runtime.Remote;

/// <summary>
/// 呼叫主機上的 runtime host（<c>Ymir.RuntimeHost</c>，ADR-0008）。API 在容器內執行時使用：API 本身不執行 Podman、
/// 不掛載 container runtime socket，也不知道任何 host 路徑；runtime host 只接受 user id 與 runtime 內的程序規格。
/// </summary>
internal sealed class RemoteRuntimeManager : IAgentRuntimeManager, IDisposable
{
    private readonly RuntimeHostConnection _connection;
    private readonly ILogger<RemoteRuntimeManager> _logger;
    private readonly ConcurrentDictionary<Guid, Guid> _usersByRuntime = new();

    public RemoteRuntimeManager(IOptions<RuntimeOptions> options, ILogger<RemoteRuntimeManager> logger)
    {
        var remote = options.Value.Remote;
        var endpoint = RuntimeHostEndpoint.Parse(remote.Endpoint, "VibeMaker:Runtime:Remote:Endpoint");
        RuntimeHostProtocol.EnsureTokenIsStrong(remote.Token, "VibeMaker:Runtime:Remote:Token");
        _connection = new RuntimeHostConnection(endpoint, remote.Token!);
        _logger = logger;
    }

    public async Task<RuntimeInfo> EnsureRuntimeAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var response = await _connection.Http.PostAsync(new Uri(RuntimeHostProtocol.RuntimePath(userId), UriKind.Relative), null, cancellationToken)
            .ConfigureAwait(false);
        return await ReadRuntimeAsync(response, "ensure", cancellationToken).ConfigureAwait(false);
    }

    public async Task StartAsync(Guid runtimeId, CancellationToken cancellationToken)
    {
        using var response = await _connection.Http.PostAsync(new Uri(RuntimeHostProtocol.StartPath(UserOf(runtimeId)), UriKind.Relative), null, cancellationToken)
            .ConfigureAwait(false);
        await ReadRuntimeAsync(response, "start", cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(Guid runtimeId, CancellationToken cancellationToken)
    {
        using var response = await _connection.Http.PostAsync(new Uri(RuntimeHostProtocol.StopPath(UserOf(runtimeId)), UriKind.Relative), null, cancellationToken)
            .ConfigureAwait(false);
        await ReadRuntimeAsync(response, "stop", cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid runtimeId, CancellationToken cancellationToken)
    {
        var userId = UserOf(runtimeId);
        using var response = await _connection.Http.DeleteAsync(new Uri(RuntimeHostProtocol.RuntimePath(userId), UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(response, "delete");
        _usersByRuntime.TryRemove(runtimeId, out _);
    }

    public async Task<RuntimeInfo> GetStatusAsync(Guid runtimeId, CancellationToken cancellationToken)
    {
        using var response = await _connection.Http.GetAsync(new Uri(RuntimeHostProtocol.RuntimePath(UserOf(runtimeId)), UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);
        return await ReadRuntimeAsync(response, "status", cancellationToken).ConfigureAwait(false);
    }

    public async Task<IRuntimeProcess> StartProcessAsync(Guid runtimeId, RuntimeProcessSpec spec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();
        // 與其他 provider 一致：不合法的規格在本地就拒絕，不送到 runtime host。
        if (RuntimeHostProtocol.Validate(spec) is { } error)
        {
            throw new ArgumentException(error, nameof(spec));
        }

        var socket = await _connection.ConnectWebSocketAsync(RuntimeHostProtocol.ProcessPath(UserOf(runtimeId)), cancellationToken).ConfigureAwait(false);
        try
        {
            return await RemoteRuntimeProcess.StartAsync(socket, spec, _logger, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public void Dispose() => _connection.Dispose();

    private async Task<RuntimeInfo> ReadRuntimeAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        EnsureSuccess(response, operation);
        var message = await response.Content.ReadFromJsonAsync<RuntimeInfoMessage>(RuntimeHostProtocol.JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Runtime host {operation} returned an empty response.");
        var runtime = message.ToRuntimeInfo();
        _usersByRuntime[runtime.RuntimeId] = runtime.UserId;
        return runtime;
    }

    private void EnsureSuccess(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // 細節只在 runtime host 的 log；這裡只記錄狀態碼。
        _logger.LogError("Runtime host {Operation} failed with status {StatusCode}", operation, (int)response.StatusCode);
        var reason = response.StatusCode == HttpStatusCode.Unauthorized ? " (check VibeMaker:Runtime:Remote:Token)" : string.Empty;
        throw new InvalidOperationException($"Runtime host {operation} failed with status {(int)response.StatusCode}{reason}.");
    }

    /// <summary>
    /// Runtime host 以 user id 為唯一輸入；runtime id 只是 API 端的識別（由 <see cref="EnsureRuntimeAsync"/> 取得）。
    /// API 重新啟動後，下一次 execution 的 EnsureRuntime 會重新建立對應。
    /// </summary>
    private Guid UserOf(Guid runtimeId) =>
        _usersByRuntime.TryGetValue(runtimeId, out var userId)
            ? userId
            : throw new InvalidOperationException($"Runtime {runtimeId} not found.");
}
