using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ymir.Testing.FakeMcp;

/// <summary>
/// 最小的 streamable HTTP MCP server（JSON 回應），提供一個 <c>echo</c> 工具，用來驗證 Ymir MCP Gateway（ADR-0012 B 階段 2）。
/// 可要求後端憑證（<paramref name="requiredToken"/>），並記錄收到的 Authorization 標頭，讓測試確認 gateway 換成後端憑證、沒有轉送 Agent 的 token。
/// </summary>
public sealed class FakeMcpServer : IAsyncDisposable
{
    public const string EchoTool = "echo";

    private readonly WebApplication _app;

    private FakeMcpServer(WebApplication app, Uri rootUrl)
    {
        _app = app;
        McpUrl = new Uri(rootUrl, "mcp");
    }

    public Uri McpUrl { get; }

    /// <summary>收到的 Authorization 標頭（依序）。</summary>
    public IReadOnlyCollection<string> AuthorizationHeaders => _app.Services.GetRequiredService<FakeMcpLog>().Authorizations;

    public static async Task<FakeMcpServer> StartAsync(string? requiredToken = null, string url = "http://127.0.0.1:0", CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(url);
        builder.Services.AddSingleton<FakeMcpLog>();
        var app = builder.Build();
        app.MapPost("/mcp", (HttpContext context, FakeMcpLog log) => HandleAsync(context, log, requiredToken));
        app.MapGet("/mcp", () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed));
        app.MapDelete("/mcp", () => Results.Ok());
        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        var address = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First();
        return new FakeMcpServer(app, new Uri(address.TrimEnd('/') + "/"));
    }

    private static async Task<IResult> HandleAsync(HttpContext context, FakeMcpLog log, string? requiredToken)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        log.Authorizations.Enqueue(authorization);
        if (requiredToken is not null && authorization != $"Bearer {requiredToken}")
        {
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        }

        var message = await JsonNode.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted).ConfigureAwait(false) as JsonObject;
        var method = message?["method"]?.GetValue<string>();
        var id = message?["id"]?.DeepClone();
        if (id is null)
        {
            return Results.StatusCode(StatusCodes.Status202Accepted); // notification
        }

        JsonNode result = method switch
        {
            "initialize" => new JsonObject
            {
                ["protocolVersion"] = message?["params"]?["protocolVersion"]?.GetValue<string>() ?? "2025-06-18",
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                ["serverInfo"] = new JsonObject { ["name"] = "ymir-fake-mcp", ["version"] = "1.0.0" },
            },
            "tools/list" => new JsonObject
            {
                ["tools"] = new JsonArray(new JsonObject
                {
                    ["name"] = EchoTool,
                    ["description"] = "Echo the given text back.",
                    ["inputSchema"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject { ["text"] = new JsonObject { ["type"] = "string" } },
                        ["required"] = new JsonArray("text"),
                    },
                }),
            },
            "tools/call" => new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = $"echo: {message?["params"]?["arguments"]?["text"]?.GetValue<string>()}",
                }),
            },
            _ => new JsonObject(),
        };

        var response = method is "initialize" or "tools/list" or "tools/call" or "ping"
            ? new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }
            : new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = -32601, ["message"] = "Method not found" } };
        return Results.Text(response.ToJsonString(), "application/json");
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }
}

internal sealed class FakeMcpLog
{
    public ConcurrentQueue<string> Authorizations { get; } = new();
}
