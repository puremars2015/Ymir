using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ymir.VibeMaker.Application.Knowledge;

namespace Ymir.VibeMaker.Infrastructure.Knowledge;

/// <summary>經 LiteLLM 的 OpenAI 相容 <c>/v1/embeddings</c>（ADR-0014 §3）；使用呼叫端給的使用者 virtual key，不保存。</summary>
internal sealed partial class LiteLlmEmbeddingClient(HttpClient http, KnowledgeEndpoint endpoint, ILogger<LiteLlmEmbeddingClient> logger) : IEmbeddingClient
{
    public const string HttpClientName = "Ymir.Knowledge.Embeddings";

    public async Task<IReadOnlyList<float[]>> EmbedAsync(string apiKey, string model, IReadOnlyList<string> inputs, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Resolve("embeddings"))
        {
            Content = JsonContent.Create(new { model, input = inputs }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new KnowledgeException("暫時無法連線到 Embedding 服務，請稍後重試。", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                LogFailed(logger, (int)response.StatusCode, model);
                throw new KnowledgeException("Embedding 服務回應錯誤，請稍後重試。");
            }

            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return [.. document.RootElement.GetProperty("data").EnumerateArray()
                .OrderBy(d => d.TryGetProperty("index", out var index) ? index.GetInt32() : 0)
                .Select(d => d.GetProperty("embedding").EnumerateArray().Select(v => v.GetSingle()).ToArray())];
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Embedding request for model {Model} returned {StatusCode}")]
    private static partial void LogFailed(ILogger logger, int statusCode, string model);
}

/// <summary>API 呼叫 LiteLLM 的位址（ADR-0014 §3）：<c>VibeMaker:Rag:BaseUrl</c>，否則 <c>VibeMaker:LiteLlm:BaseUrl</c>，否則 <c>VibeMaker:Pi:ModelBaseUrl</c>。</summary>
internal sealed record KnowledgeEndpoint(Uri BaseUrl)
{
    /// <summary>OpenAI 相容路徑：base 以 <c>/v1</c> 結尾時直接接，否則補上 <c>/v1</c>。</summary>
    public Uri Resolve(string path)
    {
        var root = BaseUrl.ToString().TrimEnd('/');
        return new Uri(root.EndsWith("/v1", StringComparison.Ordinal) ? $"{root}/{path}" : $"{root}/v1/{path}");
    }
}
