using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Ymir.Testing.FakeLlm;

/// <summary>OpenAI Chat Completions 相容端點（只實作 Pi 需要的部分），以及 LiteLLM key management 的最小子集。</summary>
public static class FakeLlmEndpoints
{
    public const string ModelId = "fake-model";

    public static IEndpointRouteBuilder MapFakeLlm(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/models", () => Results.Json(new
        {
            @object = "list",
            data = new[] { new { id = ModelId, @object = "model", owned_by = "ymir" } },
        }));

        endpoints.MapPost("/v1/chat/completions", HandleChatCompletionsAsync);
        endpoints.MapPost("/key/generate", HandleGenerateKeyAsync);
        endpoints.MapPost("/key/delete", HandleDeleteKeyAsync);
        return endpoints;
    }

    private static string? BearerToken(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.Ordinal) ? header["Bearer ".Length..] : null;
    }

    private static async Task<JsonObject> ReadObjectAsync(HttpContext context) =>
        await JsonNode.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted) as JsonObject
        ?? throw new BadHttpRequestException("JSON object expected");

    private static async Task HandleGenerateKeyAsync(HttpContext context)
    {
        var state = context.RequestServices.GetRequiredService<FakeLlmState>();
        if (state.MasterKey is null || BearerToken(context) != state.MasterKey)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = new { message = "Authentication Error, invalid master key" } });
            return;
        }

        var request = await ReadObjectAsync(context);
        var (key, token) = state.Issue(request);
        var seconds = long.TryParse(request["duration"]?.GetValue<string>()?.TrimEnd('s'), out var parsed) ? parsed : 86400;
        await context.Response.WriteAsJsonAsync(new
        {
            key,
            token,
            key_alias = request["key_alias"]?.GetValue<string>(),
            expires = DateTimeOffset.UtcNow.AddSeconds(seconds).ToString("O"),
        });
    }

    private static async Task HandleDeleteKeyAsync(HttpContext context)
    {
        var state = context.RequestServices.GetRequiredService<FakeLlmState>();
        if (state.MasterKey is null || BearerToken(context) != state.MasterKey)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var request = await ReadObjectAsync(context);
        var deleted = (request["keys"] as JsonArray ?? []).Select(k => k?.GetValue<string>()).Where(k => k is not null && state.Revoke(k)).ToList();
        await context.Response.WriteAsJsonAsync(new { deleted_keys = deleted });
    }

    private static async Task HandleChatCompletionsAsync(HttpContext context)
    {
        var state = context.RequestServices.GetRequiredService<FakeLlmState>();
        var request = await ReadObjectAsync(context);
        var apiKey = BearerToken(context);
        state.Record(request, apiKey);

        if (state.MasterKey is not null && !state.IsActive(apiKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = new { message = "Invalid or revoked virtual key", type = "auth_error" } });
            return;
        }

        var reply = FakeLlmScript.Decide(request);
        if (reply.ErrorStatusCode is { } status)
        {
            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(new { error = new { message = reply.ErrorMessage, type = "server_error" } });
            return;
        }

        var stream = request["stream"]?.GetValue<bool>() ?? false;
        if (stream)
        {
            await WriteStreamAsync(context, reply);
        }
        else
        {
            await context.Response.WriteAsJsonAsync(BuildNonStreamingResponse(reply));
        }
    }

    private static async Task WriteStreamAsync(HttpContext context, FakeLlmReply reply)
    {
        var ct = context.RequestAborted;
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";

        var id = $"chatcmpl-{Guid.NewGuid():N}";
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        async Task SendAsync(JsonObject delta, string? finishReason)
        {
            var chunk = new JsonObject
            {
                ["id"] = id,
                ["object"] = "chat.completion.chunk",
                ["created"] = created,
                ["model"] = ModelId,
                ["choices"] = new JsonArray(new JsonObject
                {
                    ["index"] = 0,
                    ["delta"] = delta,
                    ["finish_reason"] = finishReason,
                }),
            };
            await context.Response.WriteAsync($"data: {chunk.ToJsonString()}\n\n", ct);
            await context.Response.Body.FlushAsync(ct);
        }

        await SendAsync(new JsonObject { ["role"] = "assistant", ["content"] = string.Empty }, null);
        foreach (var text in reply.TextChunks)
        {
            if (reply.DelayPerChunk > TimeSpan.Zero)
            {
                await Task.Delay(reply.DelayPerChunk, ct);
            }

            await SendAsync(new JsonObject { ["content"] = text }, null);
        }

        if (reply.ToolName is not null)
        {
            await SendAsync(new JsonObject
            {
                ["tool_calls"] = new JsonArray(new JsonObject
                {
                    ["index"] = 0,
                    ["id"] = $"call_{Guid.NewGuid():N}"[..24],
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = reply.ToolName,
                        ["arguments"] = reply.ToolArguments!.ToJsonString(),
                    },
                }),
            }, null);
            await SendAsync(new JsonObject(), "tool_calls");
        }
        else
        {
            await SendAsync(new JsonObject(), "stop");
        }

        var usage = new JsonObject
        {
            ["id"] = id,
            ["object"] = "chat.completion.chunk",
            ["created"] = created,
            ["model"] = ModelId,
            ["choices"] = new JsonArray(),
            ["usage"] = new JsonObject { ["prompt_tokens"] = 10, ["completion_tokens"] = 5, ["total_tokens"] = 15 },
        };
        await context.Response.WriteAsync($"data: {usage.ToJsonString()}\n\n", ct);
        await context.Response.WriteAsync("data: [DONE]\n\n", ct);
        await context.Response.Body.FlushAsync(ct);
    }

    private static JsonObject BuildNonStreamingResponse(FakeLlmReply reply)
    {
        var message = new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = string.Concat(reply.TextChunks),
        };
        if (reply.ToolName is not null)
        {
            message["tool_calls"] = new JsonArray(new JsonObject
            {
                ["id"] = $"call_{Guid.NewGuid():N}"[..24],
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = reply.ToolName,
                    ["arguments"] = reply.ToolArguments!.ToJsonString(),
                },
            });
        }

        return new JsonObject
        {
            ["id"] = $"chatcmpl-{Guid.NewGuid():N}",
            ["object"] = "chat.completion",
            ["created"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["model"] = ModelId,
            ["choices"] = new JsonArray(new JsonObject
            {
                ["index"] = 0,
                ["message"] = message,
                ["finish_reason"] = reply.ToolName is null ? "stop" : "tool_calls",
            }),
            ["usage"] = new JsonObject { ["prompt_tokens"] = 10, ["completion_tokens"] = 5, ["total_tokens"] = 15 },
        };
    }
}
