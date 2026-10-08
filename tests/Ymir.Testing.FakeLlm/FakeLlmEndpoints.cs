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
        endpoints.MapPost("/v1/embeddings", HandleEmbeddingsAsync);
        // 同 LiteLLM：不需要金鑰的存活檢查
        endpoints.MapGet("/health/liveliness", () => Results.Text("I'm alive!"));
        endpoints.MapPost("/key/generate", HandleGenerateKeyAsync);
        endpoints.MapPost("/key/delete", HandleDeleteKeyAsync);
        endpoints.MapPost("/user/new", HandleNewUserAsync);
        endpoints.MapPost("/user/update", HandleUpdateUserAsync);
        endpoints.MapGet("/user/info", HandleUserInfoAsync);
        endpoints.MapGet("/user/daily/activity", HandleDailyActivityAsync);
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

    private static bool IsMaster(HttpContext context, FakeLlmState state)
    {
        if (state.MasterKey is not null && BearerToken(context) == state.MasterKey)
        {
            return true;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return false;
    }

    private static decimal? ReadBudget(JsonObject request) =>
        request["max_budget"] is JsonValue value ? value.GetValue<decimal>() : null;

    private static async Task HandleNewUserAsync(HttpContext context)
    {
        var state = context.RequestServices.GetRequiredService<FakeLlmState>();
        if (!IsMaster(context, state))
        {
            return;
        }

        var request = await ReadObjectAsync(context);
        var userId = request["user_id"]?.GetValue<string>() ?? Guid.NewGuid().ToString();
        if (!state.LiteLlmUsers.TryCreate(userId, ReadBudget(request), request["budget_duration"]?.GetValue<string>()))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = new { message = "User already exists" } });
            return;
        }

        await context.Response.WriteAsJsonAsync(new { user_id = userId });
    }

    private static async Task HandleUpdateUserAsync(HttpContext context)
    {
        var state = context.RequestServices.GetRequiredService<FakeLlmState>();
        if (!IsMaster(context, state))
        {
            return;
        }

        var request = await ReadObjectAsync(context);
        if (state.LiteLlmUsers.Find(request["user_id"]?.GetValue<string>()) is not { } user)
        {
            // LiteLLM 對不存在的使用者回 4xx
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = new { message = "User not found" } });
            return;
        }

        user.MaxBudget = ReadBudget(request);
        user.BudgetDuration = request["budget_duration"]?.GetValue<string>();
        await context.Response.WriteAsJsonAsync(new { user_id = user.UserId });
    }

    private static async Task HandleUserInfoAsync(HttpContext context)
    {
        var state = context.RequestServices.GetRequiredService<FakeLlmState>();
        if (!IsMaster(context, state))
        {
            return;
        }

        if (state.LiteLlmUsers.Find(context.Request.Query["user_id"]) is not { } user)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await context.Response.WriteAsJsonAsync(new
        {
            user_id = user.UserId,
            user_info = new { user_id = user.UserId, spend = user.Spend, max_budget = user.MaxBudget, budget_duration = user.BudgetDuration, budget_reset_at = user.BudgetResetAt.ToString("O") },
            keys = Array.Empty<object>(),
        });
    }

    /// <summary>格式同 LiteLLM 的 <c>/user/daily/activity</c>：results[].metrics 與 metadata 合計。</summary>
    private static async Task HandleDailyActivityAsync(HttpContext context)
    {
        var state = context.RequestServices.GetRequiredService<FakeLlmState>();
        if (!IsMaster(context, state))
        {
            return;
        }

        var from = DateOnly.Parse(context.Request.Query["start_date"].ToString(), System.Globalization.CultureInfo.InvariantCulture);
        var to = DateOnly.Parse(context.Request.Query["end_date"].ToString(), System.Globalization.CultureInfo.InvariantCulture);
        var days = state.LiteLlmUsers.Find(context.Request.Query["user_id"])?.Daily
            .Where(d => d.Key >= from && d.Key <= to)
            .OrderBy(d => d.Key)
            .Select(d => new
            {
                date = d.Key.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                metrics = new
                {
                    spend = d.Value.Spend,
                    prompt_tokens = d.Value.Requests * FakeLiteLlmUsers.PromptTokensPerRequest,
                    completion_tokens = d.Value.Requests * FakeLiteLlmUsers.CompletionTokensPerRequest,
                    total_tokens = d.Value.Requests * (FakeLiteLlmUsers.PromptTokensPerRequest + FakeLiteLlmUsers.CompletionTokensPerRequest),
                    api_requests = d.Value.Requests,
                },
            })
            .ToList() ?? [];
        await context.Response.WriteAsJsonAsync(new
        {
            results = days,
            metadata = new
            {
                total_spend = days.Sum(d => d.metrics.spend),
                total_prompt_tokens = days.Sum(d => d.metrics.prompt_tokens),
                total_completion_tokens = days.Sum(d => d.metrics.completion_tokens),
                total_api_requests = days.Sum(d => d.metrics.api_requests),
                page = 1,
                total_pages = 1,
                has_more = false,
            },
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

    /// <summary>
    /// OpenAI 相容的 embeddings（RAG 測試用，ADR-0014）：以字元 bigram 雜湊到 <see cref="FakeEmbedding.Dimensions"/> 維並正規化，
    /// 結果固定、共享詞彙的文字相似度較高，可以驗證檢索與引用。
    /// </summary>
    private static async Task HandleEmbeddingsAsync(HttpContext context)
    {
        var request = await ReadObjectAsync(context);
        var input = request["input"];
        IReadOnlyList<string> texts = input switch
        {
            JsonArray array => [.. array.Select(i => i?.GetValue<string>() ?? string.Empty)],
            JsonValue value => [value.GetValue<string>()],
            _ => [],
        };
        context.RequestServices.GetRequiredService<FakeLlmState>().RecordEmbeddings(texts.Count);
        await context.Response.WriteAsJsonAsync(new JsonObject
        {
            ["object"] = "list",
            ["model"] = request["model"]?.GetValue<string>() ?? "fake-embedding",
            ["data"] = new JsonArray([.. texts.Select((text, index) => (JsonNode)new JsonObject
            {
                ["object"] = "embedding",
                ["index"] = index,
                ["embedding"] = new JsonArray([.. FakeEmbedding.Embed(text).Select(v => (JsonNode)JsonValue.Create(v))]),
            })]),
        });
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

        // LiteLLM 的使用者預算：本期花費達到上限時拒絕（ADR-0011）
        var liteLlmUser = state.MasterKey is null ? null : state.UserOfKey(apiKey);
        if (liteLlmUser is { IsOverBudget: true })
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new { message = $"Budget has been exceeded! Current cost: {liteLlmUser.Spend}, Max budget: {liteLlmUser.MaxBudget}", type = "budget_exceeded" },
            });
            return;
        }

        liteLlmUser?.RecordRequest();
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
