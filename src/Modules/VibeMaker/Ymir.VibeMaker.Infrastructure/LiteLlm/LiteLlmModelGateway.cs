using System.Globalization;
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
/// </summary>
internal sealed class LiteLlmModelGateway(HttpClient httpClient, IOptions<LiteLlmOptions> options, TimeProvider timeProvider, ILogger<LiteLlmModelGateway> logger)
    : IModelGateway
{
    public const string HttpClientName = "litellm";

    public async Task<RuntimeModelCredential> IssueRuntimeCredentialAsync(RuntimeCredentialRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = timeProvider.GetUtcNow();
        var body = new JsonObject
        {
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

    private async Task<JsonObject> SendAsync(string path, JsonObject body, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (settings.BaseUrl is null || !settings.IsConfigured)
        {
            throw new ModelCredentialException("VibeMaker:LiteLlm:BaseUrl and MasterKey must be configured.");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.BaseUrl, path))
        {
            Content = JsonContent.Create(body),
        };
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
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                logger.LogError("LiteLLM {Path} failed with {StatusCode}: {Detail}", path, (int)response.StatusCode, detail.Length > 500 ? detail[..500] : detail);
                throw new ModelCredentialException($"LiteLLM {path} failed with status {(int)response.StatusCode}.");
            }

            return await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken).ConfigureAwait(false)
                ?? throw new ModelCredentialException($"LiteLLM {path} returned an empty body.");
        }
    }
}
