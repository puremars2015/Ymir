using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ymir.VibeMaker.Application.Knowledge;

namespace Ymir.VibeMaker.Infrastructure.Knowledge;

/// <summary>知識庫問答的回答模型呼叫（ADR-0014 §8）：OpenAI 相容的 <c>/v1/chat/completions</c>，不串流。</summary>
internal sealed partial class LiteLlmAnswerClient(HttpClient http, KnowledgeEndpoint endpoint, ILogger<LiteLlmAnswerClient> logger) : IKnowledgeAnswerClient
{
    public const string HttpClientName = "Ymir.Knowledge.Answers";

    public async Task<string> CompleteAsync(string apiKey, string model, string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Resolve("chat/completions"))
        {
            Content = JsonContent.Create(new
            {
                model,
                stream = false,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt },
                },
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new KnowledgeException("暫時無法連線到模型服務，請稍後再試。", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                LogFailed(logger, (int)response.StatusCode, model);
                throw new KnowledgeException((int)response.StatusCode == 400
                    ? "模型服務拒絕了這次請求（可能已達用量上限），請洽管理員。"
                    : "模型服務回應錯誤，請稍後再試。");
            }

            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Knowledge answer request for model {Model} returned {StatusCode}")]
    private static partial void LogFailed(ILogger logger, int statusCode, string model);
}
