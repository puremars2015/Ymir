using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Ymir.VibeMaker.Application.Extensions;

namespace Ymir.VibeMaker.Infrastructure.PiAgent;

internal sealed class PersonalMcpSecrets(IDataProtectionProvider provider) : IPersonalMcpSecrets
{
    private IDataProtector For(Guid userId) => provider.CreateProtector("Ymir.PersonalMcp.v1", userId.ToString("N"));
    public string Protect(Guid userId, string token) => For(userId).Protect(token);
    public string Unprotect(Guid userId, string ciphertext) => For(userId).Unprotect(ciphertext);
}

/// <summary>API 的測試 transport 不允許成為內網代理；DNS 驗證與實際 socket 連線不可分離。</summary>
internal sealed class PersonalMcpProbe(HttpClient http) : IPersonalMcpProbe
{
    public static SocketsHttpHandler Handler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(4),
        MaxResponseHeadersLength = 16,
        ConnectCallback = ConnectAsync,
    };

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        if (context.DnsEndPoint.Port != 443) throw new HttpRequestException("Public HTTPS only.");
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct).ConfigureAwait(false);
        if (addresses.Length == 0 || addresses.Any(a => !IsPublicAddress(a))) throw new HttpRequestException("Destination rejected.");
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(addresses[0], 443), ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    internal static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return b[0] is not (0 or 10 or 127) && b[0] < 224
                && !(b[0] == 100 && b[1] is >= 64 and <= 127)
                && !(b[0] == 169 && b[1] == 254)
                && !(b[0] == 172 && b[1] is >= 16 and <= 31)
                && !(b[0] == 192 && (b[1] is 0 or 168 || b[1] == 88 && b[2] == 99))
                && !(b[0] == 198 && (b[1] is 18 or 19 || b[1] == 51 && b[2] == 100))
                && !(b[0] == 203 && b[1] == 0 && b[2] == 113);
        }
        return address.AddressFamily == AddressFamily.InterNetworkV6 && (b[0] & 0xe0) == 0x20
            && !(b[0] == 0x20 && b[1] == 0x01 && b[2] < 2)
            && !(b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8)
            && !(b[0] == 0x20 && b[1] == 0x02)
            && !(b[0] == 0x3f && b[1] == 0xff);
    }

    public async Task<bool> TestAsync(Uri url, string? token, CancellationToken cancellationToken)
    {
        if (!PersonalMcpService.IsValidUrl(url.AbsoluteUri)) return false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Accept.ParseAdd("application/json, text/event-stream");
            if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new { protocolVersion = "2025-03-26", capabilities = new { }, clientInfo = new { name = "ymir-connection-test", version = "1.0" } },
            });
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return false;
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            // Read at most one bounded response; never display server-provided errors or tool descriptions.
            var buffer = new byte[65537];
            var used = 0;
            var sse = response.Content.Headers.ContentType?.MediaType == "text/event-stream";
            while (used < buffer.Length)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(used), timeout.Token).ConfigureAwait(false);
                if (count == 0) break;
                used += count;
                if (used <= 65536 && sse && TryInitializeResponse(Encoding.UTF8.GetString(buffer, 0, used), true)) return true;
            }
            return used <= 65536 && TryInitializeResponse(Encoding.UTF8.GetString(buffer, 0, used), sse);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException or CryptographicException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }

    private static bool TryInitializeResponse(string text, bool sse)
    {
        if (sse)
        {
            // Only complete SSE events can be consumed; ignore comments/notifications and stop at initialize.
            var events = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n\n");
            return events.Take(events.Length - 1).Any(e => TryInitializeResponse(string.Join('\n', e.Split('\n').Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].TrimStart())), false));
        }
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("jsonrpc", out var version) && version.ValueKind == JsonValueKind.String && version.GetString() == "2.0"
                && root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out var number) && number == 1
                && root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object
                && result.TryGetProperty("protocolVersion", out var protocol) && protocol.ValueKind == JsonValueKind.String
                && result.TryGetProperty("capabilities", out var capabilities) && capabilities.ValueKind == JsonValueKind.Object
                && result.TryGetProperty("serverInfo", out var info) && info.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException) { return false; }
    }
}
