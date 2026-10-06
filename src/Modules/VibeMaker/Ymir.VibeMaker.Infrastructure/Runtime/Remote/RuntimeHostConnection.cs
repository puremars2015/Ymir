using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Net.WebSockets;

namespace Ymir.VibeMaker.Infrastructure.Runtime.Remote;

/// <summary>
/// 連到 runtime host 的 HTTP / WebSocket 連線（ADR-0008）。Unix socket 以 <see cref="SocketsHttpHandler.ConnectCallback"/> 連線，
/// URI 的主機名稱只是佔位（<c>localhost</c>）。
/// </summary>
internal sealed class RuntimeHostConnection : IDisposable
{
    private static readonly Uri s_unixBaseUri = new("http://localhost/");

    private readonly SocketsHttpHandler _handler;
    private readonly string _token;

    public RuntimeHostConnection(RuntimeHostEndpoint endpoint, string token)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        _token = token;
        BaseUri = endpoint.HttpUri ?? s_unixBaseUri;
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

        Http = new HttpClient(_handler, disposeHandler: false) { BaseAddress = BaseUri, Timeout = TimeSpan.FromMinutes(5) };
        Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(RuntimeHostProtocol.AuthorizationScheme, token);
    }

    public Uri BaseUri { get; }

    /// <summary>一般操作（建立 / 啟動 / 停止 / 查詢）；建立 container 可能要拉 image，逾時放寬到 5 分鐘。</summary>
    public HttpClient Http { get; }

    public async Task<ClientWebSocket> ConnectWebSocketAsync(string path, CancellationToken cancellationToken)
    {
        var builder = new UriBuilder(new Uri(BaseUri, path)) { Scheme = "ws" };
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", $"{RuntimeHostProtocol.AuthorizationScheme} {_token}");
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        try
        {
            using var invoker = new HttpMessageInvoker(_handler, disposeHandler: false);
            await socket.ConnectAsync(builder.Uri, invoker, cancellationToken).ConfigureAwait(false);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Http.Dispose();
        _handler.Dispose();
    }
}
