using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ymir.VibeMaker.Application.Models;

namespace Ymir.VibeMaker.Infrastructure.LiteLlm;

/// <summary>
/// 以 LiteLLM 的 key management API 發放 / 撤銷 virtual key（ADR-0004）。需要 LiteLLM 接 PostgreSQL。
/// master key 只出現在這個類別送往 LiteLLM 的 Authorization header。
/// <para>
/// key 帶 LiteLLM 的 <c>user_id</c>（= Ymir user id），LiteLLM 因此依使用者彙總花費並套用使用者的每月預算；
/// 用量讀 <c>/user/daily/activity</c>，本期花費與預算讀 <c>/user/info</c>（ADR-0011）。
/// </para>
/// </summary>
internal sealed class LiteLlmModelGateway(HttpClient httpClient, IOptions<LiteLlmOptions> options, TimeProvider timeProvider, ILogger<LiteLlmModelGateway> logger)
    : IModelGateway
{
    public const string HttpClientName = "litellm";

    /// <summary>LiteLLM 的預算週期；Ymir 的「每月預算」以 30 天為一期。</summary>
    internal const string BudgetDuration = "30d";

    private const int UsageConcurrency = 4;
    private const int UsagePageSize = 100;
    private const int MaxUsagePages = 20;

    public bool SupportsUsage => true;

    public async Task<RuntimeModelCredential> IssueRuntimeCredentialAsync(RuntimeCredentialRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // 先確保 LiteLLM 有這位使用者並套用目前的預算；key 掛在使用者底下，花費才會依使用者彙總。
        await ApplyUserBudgetAsync(request.UserId, request.MonthlyBudget, cancellationToken).ConfigureAwait(false);

        var now = timeProvider.GetUtcNow();
        var body = new JsonObject
        {
            ["user_id"] = request.UserId.ToString("D"),
            ["models"] = new JsonArray(request.AllowedModels.Select(m => (JsonNode)m).ToArray()),
            ["duration"] = $"{(long)request.Lifetime.TotalSeconds}s",
            // alias 方便在 LiteLLM 後台辨識；加上時間避免換發時與舊 key 重複。
            ["key_alias"] = $"ymir-user-{request.UserId:N}-{now:yyyyMMddHHmmss}",
            ["metadata"] = new JsonObject
            {
                ["ymir_user_id"] = request.UserId.ToString("D"),
                ["ymir_runtime_id"] = request.RuntimeId.ToString("D"),
            },
        };
        if (request.MaxBudget is { } budget)
        {
            body["max_budget"] = budget;
        }

        var response = await SendAsync("key/generate", body, cancellationToken).ConfigureAwait(false);
        var key = response["key"]?.GetValue<string>();
        if (string.IsNullOrEmpty(key))
        {
            throw new ModelCredentialException("LiteLLM /key/generate returned no key.");
        }

        // token 是 LiteLLM 保存的 hashed key，用於撤銷；舊版沒有 token 時退回用 key 本身撤銷。
        var keyId = response["token"]?.GetValue<string>() is { Length: > 0 } token ? token : key;
        var expiresAt = response["expires"]?.GetValue<string>() is { } expires
            && DateTimeOffset.TryParse(expires, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : now + request.Lifetime;
        return new RuntimeModelCredential(keyId, key, expiresAt);
    }

    public async Task RevokeRuntimeCredentialAsync(string keyId, CancellationToken cancellationToken) =>
        await SendAsync("key/delete", new JsonObject { ["keys"] = new JsonArray(keyId) }, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// 建立或更新 LiteLLM 使用者並設定預算（upsert）：先 <c>/user/update</c>，使用者不存在時改 <c>/user/new</c>。
    /// 預算 null 表示不限制（清除 max_budget 與週期）。
    /// </summary>
    public async Task ApplyUserBudgetAsync(Guid userId, decimal? monthlyBudget, CancellationToken cancellationToken)
    {
        JsonObject Body() => new()
        {
            ["user_id"] = userId.ToString("D"),
            ["max_budget"] = monthlyBudget is { } budget ? JsonValue.Create(budget) : null,
            ["budget_duration"] = monthlyBudget is null ? null : BudgetDuration,
        };

        var (status, _) = await RequestAsync(HttpMethod.Post, "user/update", Body(), cancellationToken).ConfigureAwait(false);
        if (status is >= HttpStatusCode.OK and < HttpStatusCode.Ambiguous)
        {
            return;
        }

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden || (int)status >= 500)
        {
            throw new ModelCredentialException($"LiteLLM user/update failed with status {(int)status}.");
        }

        // 使用者不存在（LiteLLM 回 4xx）：建立使用者，不自動產生 key（key 由 Ymir 另外發）。
        var create = Body();
        create["auto_create_key"] = false;
        await SendAsync("user/new", create, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ModelBudgetStatus?> GetBudgetStatusAsync(Guid userId, CancellationToken cancellationToken)
    {
        var (status, body) = await RequestAsync(HttpMethod.Get, $"user/info?user_id={userId:D}", null, cancellationToken).ConfigureAwait(false);
        if (status is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return null; // 還沒用過模型，LiteLLM 沒有這位使用者
        }

        EnsureSuccess("user/info", status);
        return body is null ? null : ParseBudget(body);
    }

    public async Task<IReadOnlyDictionary<Guid, ModelUserUsage>> GetUsageAsync(IReadOnlyCollection<Guid> userIds, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        var results = new System.Collections.Concurrent.ConcurrentDictionary<Guid, ModelUserUsage>();
        using var throttle = new SemaphoreSlim(UsageConcurrency);
        await Task.WhenAll(userIds.Distinct().Select(async userId =>
        {
            await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                results[userId] = await GetUserUsageAsync(userId, fromDate, toDate, cancellationToken).ConfigureAwait(false);
            }
            catch (ModelCredentialException ex)
            {
                // 單一使用者失敗只影響那一列；細節已在 RequestAsync 寫入 log。
                logger.LogWarning(ex, "Failed to read LiteLLM usage for user {UserId}", userId);
            }
            finally
            {
                throttle.Release();
            }
        })).ConfigureAwait(false);
        return results;
    }

    private async Task<ModelUserUsage> GetUserUsageAsync(Guid userId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
    {
        decimal spend = 0;
        long prompt = 0, completion = 0, requests = 0;
        for (var page = 1; page <= MaxUsagePages; page++)
        {
            var path = string.Create(
                CultureInfo.InvariantCulture,
                $"user/daily/activity?user_id={userId:D}&start_date={fromDate:yyyy-MM-dd}&end_date={toDate:yyyy-MM-dd}&page={page}&page_size={UsagePageSize}");
            var (status, body) = await RequestAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
            EnsureSuccess("user/daily/activity", status);
            foreach (var day in body?["results"] as JsonArray ?? [])
            {
                var metrics = day?["metrics"] as JsonObject;
                spend += ReadDecimal(metrics, "spend");
                prompt += (long)ReadDecimal(metrics, "prompt_tokens");
                completion += (long)ReadDecimal(metrics, "completion_tokens");
                requests += (long)ReadDecimal(metrics, "api_requests");
            }

            if (body?["metadata"]?["has_more"]?.GetValueKind() != System.Text.Json.JsonValueKind.True)
            {
                break;
            }
        }

        return new ModelUserUsage(spend, prompt, completion, requests, await GetBudgetStatusAsync(userId, cancellationToken).ConfigureAwait(false));
    }

    internal static ModelBudgetStatus ParseBudget(JsonObject body)
    {
        // /user/info 回傳 { user_id, user_info: { spend, max_budget, budget_reset_at, ... }, keys: [...] }
        var info = body["user_info"] as JsonObject ?? body;
        decimal? max = info["max_budget"] is JsonValue ? ReadDecimal(info, "max_budget") : null;
        var resetAt = info["budget_reset_at"]?.GetValueKind() == System.Text.Json.JsonValueKind.String
            && DateTimeOffset.TryParse(info["budget_reset_at"]!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : (DateTimeOffset?)null;
        return new ModelBudgetStatus(ReadDecimal(info, "spend"), max, resetAt);
    }

    private static decimal ReadDecimal(JsonObject? obj, string name) =>
        obj?[name] is JsonValue value && value.GetValueKind() == System.Text.Json.JsonValueKind.Number ? value.GetValue<decimal>() : 0;

    private async Task<JsonObject> SendAsync(string path, JsonObject body, CancellationToken cancellationToken)
    {
        var (status, response) = await RequestAsync(HttpMethod.Post, path, body, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(path, status);
        return response ?? throw new ModelCredentialException($"LiteLLM {path} returned an empty body.");
    }

    private static void EnsureSuccess(string path, HttpStatusCode status)
    {
        if ((int)status is < 200 or >= 300)
        {
            throw new ModelCredentialException($"LiteLLM {path} failed with status {(int)status}.");
        }
    }

    /// <summary>送出請求；失敗的回應只把前 500 字寫進 server log（可能含錯誤細節，不回傳瀏覽器）。</summary>
    private async Task<(HttpStatusCode Status, JsonObject? Body)> RequestAsync(HttpMethod method, string path, JsonObject? body, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (settings.BaseUrl is null || !settings.IsConfigured)
        {
            throw new ModelCredentialException("VibeMaker:LiteLlm:BaseUrl and MasterKey must be configured.");
        }

        using var message = new HttpRequestMessage(method, new Uri(settings.BaseUrl, path));
        if (body is not null)
        {
            message.Content = JsonContent.Create(body);
        }

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.MasterKey);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ModelCredentialException($"LiteLLM {path} is unreachable.", ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("LiteLLM {Method} {Path} returned {StatusCode}: {Detail}", method, path.Split('?')[0], (int)response.StatusCode, text.Length > 500 ? text[..500] : text);
                return (response.StatusCode, null);
            }

            try
            {
                return (response.StatusCode, text.Length == 0 ? null : JsonNode.Parse(text) as JsonObject);
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new ModelCredentialException($"LiteLLM {path} returned invalid JSON.", ex);
            }
        }
    }
}
