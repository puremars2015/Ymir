using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Ymir.VibeMaker.Application.Connectors.OneDrive;
using Ymir.VibeMaker.Application.PlatformMcp;

namespace Ymir.Api.Endpoints;

/// <summary>
/// OneDrive 外掛的 MCP server（ADR-0030 §2）：只給主機上的 MCP gateway 呼叫（streamable HTTP，JSON 回應）。
/// <list type="bullet">
/// <item>不接受 cookie、不在 OpenAPI；帶 Cloudflare 轉送標頭的請求（從網際網路經 Tunnel 進來）一律 404。</item>
/// <item>必須同時有 gateway 的後端憑證（由簽章金鑰衍生）與 Agent 的每人 token；token 要包含 <c>onedrive</c> 與 execution id。</item>
/// <item>每個請求都重新檢查 execution 執行中、帳號、能力、外掛開關與連線（<see cref="OneDriveAgentTools.ResolveAsync"/>）。</item>
/// </list>
/// </summary>
internal static class OneDriveMcpEndpoints
{
    public const string Path = "/internal/mcp/onedrive";
    public const string ForwardedTokenHeader = "X-Ymir-Mcp-Token";
    public const int MaxRequestBytes = 1024 * 1024;

    public static IEndpointRouteBuilder MapOneDriveMcpEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(Path, HandleAsync).AllowAnonymous().ExcludeFromDescription();
        endpoints.MapGet(Path, () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed)).AllowAnonymous().ExcludeFromDescription();
        endpoints.MapDelete(Path, () => Results.Ok()).AllowAnonymous().ExcludeFromDescription();
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        IOptions<McpOptions> options,
        OneDriveAgentTools tools,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var signingKey = options.Value.TokenSigningKey;
        if (!options.Value.IsEnabled || string.IsNullOrEmpty(signingKey) || context.Request.Headers.ContainsKey("Cf-Connecting-Ip"))
        {
            return Results.NotFound();
        }

        var authorization = AuthenticationHeaderValue.TryParse(context.Request.Headers.Authorization.ToString(), out var header) && header.Scheme == "Bearer"
            ? header.Parameter
            : null;
        var claims = McpGatewayToken.IsBuiltInBackendCredential(signingKey, authorization)
            ? McpGatewayToken.Validate(signingKey, context.Request.Headers[ForwardedTokenHeader].ToString(), timeProvider.GetUtcNow())
            : null;
        if (claims?.ExecutionId is not { } executionId || !claims.Servers.Contains(McpCatalog.OneDriveServerName, StringComparer.Ordinal))
        {
            return Error(StatusCodes.Status401Unauthorized, "OneDrive 的授權無效或已過期，請重新執行。");
        }

        if (context.Request.ContentLength > MaxRequestBytes)
        {
            return Error(StatusCodes.Status413PayloadTooLarge, "請求內容過大。");
        }

        var toolContext = await tools.ResolveAsync(claims.UserId, executionId, cancellationToken);
        if (toolContext is null)
        {
            // 執行已結束、外掛停用、能力撤銷或需要重新連結：不區分原因。
            return Error(StatusCodes.Status403Forbidden, "OneDrive 外掛目前無法使用（執行已結束、外掛已停用或需要重新連結）。");
        }

        JsonObject? message;
        try
        {
            message = await JsonNode.ParseAsync(context.Request.Body, cancellationToken: cancellationToken) as JsonObject;
        }
        catch (JsonException)
        {
            message = null;
        }

        if (message is null)
        {
            return Rpc(null, error: (-32700, "Parse error"));
        }

        var id = message["id"]?.DeepClone();
        var method = message["method"] is JsonValue m && m.TryGetValue<string>(out var name) ? name : null;
        if (id is null)
        {
            return Results.StatusCode(StatusCodes.Status202Accepted); // notification（例如 notifications/initialized）
        }

        switch (method)
        {
            case "initialize":
                var requested = message["params"]?["protocolVersion"] is JsonValue v && v.TryGetValue<string>(out var version) ? version : "2025-06-18";
                return Rpc(id, new JsonObject
                {
                    ["protocolVersion"] = requested,
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = "ymir-onedrive", ["version"] = "1.0.0" },
                });
            case "ping":
                return Rpc(id, new JsonObject());
            case "tools/list":
                return Rpc(id, new JsonObject
                {
                    ["tools"] = new JsonArray([.. OneDriveAgentTools.Definitions.Select(d => (JsonNode)new JsonObject
                    {
                        ["name"] = d.Name,
                        ["description"] = d.Description,
                        ["inputSchema"] = d.InputSchema.DeepClone(),
                    })]),
                });
            case "tools/call":
                var tool = message["params"]?["name"] is JsonValue t && t.TryGetValue<string>(out var toolName) ? toolName : string.Empty;
                using (var arguments = JsonDocument.Parse(message["params"]?["arguments"]?.ToJsonString() ?? "{}"))
                {
                    var result = await tools.CallAsync(toolContext, tool, arguments.RootElement, cancellationToken);
                    return Rpc(id, new JsonObject
                    {
                        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = result.Text }),
                        ["isError"] = result.IsError,
                    });
                }

            default:
                return Rpc(id, error: (-32601, "Method not found"));
        }
    }

    private static IResult Rpc(JsonNode? id, JsonObject? result = null, (int Code, string Message)? error = null)
    {
        var response = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id };
        if (error is { } e)
        {
            response["error"] = new JsonObject { ["code"] = e.Code, ["message"] = e.Message };
        }
        else
        {
            response["result"] = result ?? new JsonObject();
        }

        return Results.Text(response.ToJsonString(), "application/json");
    }

    private static IResult Error(int status, string message) => Results.Json(new { error = message }, statusCode: status);
}
