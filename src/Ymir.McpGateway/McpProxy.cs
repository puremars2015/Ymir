using System.Net.Http.Headers;
using System.Text.Json;
using Ymir.Platform.Auditing;
using Ymir.VibeMaker.Application.PlatformMcp;

namespace Ymir.McpGateway;

/// <summary>
/// 把 streamable HTTP MCP 請求轉送到目錄中的後端（ADR-0012 B.2）。
/// 只轉送 MCP 需要的標頭；後端憑證由 gateway 依目錄的 <c>credentialEnv</c> 加上，Agent 的 token 不會送到後端。
/// </summary>
internal sealed partial class McpProxy(
    IHttpClientFactory httpClientFactory,
    McpCatalog catalog,
    McpGatewayOptions options,
    IAuditLog auditLog,
    TimeProvider timeProvider,
    ILogger<McpProxy> logger)
{
    public const string BackendClientName = "Ymir.McpGateway.Backend";

    private static readonly string[] s_forwardedRequestHeaders = ["Accept", "Mcp-Session-Id", "Mcp-Protocol-Version", "Last-Event-ID"];
    private static readonly string[] s_forwardedResponseHeaders = ["Mcp-Session-Id", "Cache-Control"];

    public async Task ForwardAsync(string server, HttpContext context)
    {
        var claims = GatewayAuthentication.ClaimsOf(context)!;
        var definition = catalog.Find(server);
        if (definition is null || !claims.Servers.Contains(server, StringComparer.Ordinal))
        {
            // 目錄沒有或 token 沒有授權：Agent 只看到摘要（SA §10、§12）。
            await auditLog.WriteAsync(
                new AuditEntry(AuditActor.ForUser(claims.UserId), "mcp.access.denied", "mcp-server", Truncate(server), AuditResult.Denied, timeProvider.GetUtcNow(), null),
                context.RequestAborted).ConfigureAwait(false);
            await WriteErrorAsync(context, StatusCodes.Status403Forbidden, "沒有使用這個平台服務的權限。").ConfigureAwait(false);
            return;
        }

        byte[]? body = null;
        if (HttpMethods.IsPost(context.Request.Method))
        {
            body = await ReadBodyAsync(context.Request, options.MaxRequestBytes, context.RequestAborted).ConfigureAwait(false);
            if (body is null)
            {
                await WriteErrorAsync(context, StatusCodes.Status413PayloadTooLarge, "請求內容過大。").ConfigureAwait(false);
                return;
            }
        }

        var tools = body is null ? [] : ToolCalls(body);
        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), definition.Url);
        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var contentType)
                ? contentType
                : new MediaTypeHeaderValue("application/json");
        }

        foreach (var name in s_forwardedRequestHeaders)
        {
            if (context.Request.Headers.TryGetValue(name, out var values))
            {
                request.Headers.TryAddWithoutValidation(name, (IEnumerable<string?>)values);
            }
        }

        if (definition.CredentialEnv is { } credentialEnv)
        {
            if (Environment.GetEnvironmentVariable(credentialEnv) is not { Length: > 0 } credential)
            {
                LogMissingCredential(logger, server, credentialEnv);
                await WriteErrorAsync(context, StatusCodes.Status502BadGateway, "平台服務暫時無法使用。").ConfigureAwait(false);
                return;
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        }

        // POST 有回應時間上限；GET 是伺服器推送的 SSE 串流，跟著連線結束。
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        if (body is not null)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.BackendTimeoutSeconds)));
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.CreateClient(BackendClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !context.RequestAborted.IsCancellationRequested))
        {
            LogBackendFailed(logger, ex, server);
            await AuditToolsAsync(claims, server, tools, AuditResult.Failure).ConfigureAwait(false);
            await WriteErrorAsync(context, StatusCodes.Status502BadGateway, "平台服務暫時無法使用。").ConfigureAwait(false);
            return;
        }

        using (response)
        {
            await AuditToolsAsync(claims, server, tools, response.IsSuccessStatusCode ? AuditResult.Success : AuditResult.Failure).ConfigureAwait(false);
            context.Response.StatusCode = (int)response.StatusCode;
            if (response.Content.Headers.ContentType is { } responseType)
            {
                context.Response.ContentType = responseType.ToString();
            }

            foreach (var name in s_forwardedResponseHeaders)
            {
                if (response.Headers.TryGetValues(name, out var values))
                {
                    context.Response.Headers[name] = values.ToArray();
                }
            }

            try
            {
                await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // Agent 中斷連線（例如結束 SSE 串流）。
            }
        }
    }

    internal static async Task WriteErrorAsync(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { error = message }).ConfigureAwait(false);
    }

    /// <summary>JSON-RPC 訊息（單一或 batch）中的 <c>tools/call</c> 工具名稱；只取名稱，不讀參數。</summary>
    internal static IReadOnlyList<string> ToolCalls(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            IEnumerable<JsonElement> messages = document.RootElement.ValueKind == JsonValueKind.Array
                ? [.. document.RootElement.EnumerateArray()]
                : [document.RootElement];
            return [.. messages
                .Where(m => m.ValueKind == JsonValueKind.Object
                    && m.TryGetProperty("method", out var method) && method.ValueEquals("tools/call")
                    && m.TryGetProperty("params", out var p) && p.ValueKind == JsonValueKind.Object
                    && p.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                .Select(m => Truncate(m.GetProperty("params").GetProperty("name").GetString()!))];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task AuditToolsAsync(McpGatewayClaims claims, string server, IReadOnlyList<string> tools, AuditResult result)
    {
        foreach (var tool in tools)
        {
            // 稽核只記使用者、服務與工具名稱（ADR-0012 B.4），不記參數與回傳資料。
            await auditLog.WriteAsync(
                new AuditEntry(AuditActor.ForUser(claims.UserId), "mcp.tool.call", "mcp-server", $"{server}/{tool}", result, timeProvider.GetUtcNow(), null),
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, int limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static string Truncate(string value) => value.Length > 100 ? value[..100] : value;

    [LoggerMessage(Level = LogLevel.Error, Message = "MCP server {Server} needs credential environment variable {Variable}, which is not set")]
    private static partial void LogMissingCredential(ILogger logger, string server, string variable);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Forwarding to MCP server {Server} failed")]
    private static partial void LogBackendFailed(ILogger logger, Exception exception, string server);
}
