using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Text.Json;
using Ymir.IntegrationTests.Api;
using Ymir.VibeMaker.Contracts.Conversations;

namespace Ymir.IntegrationTests.Executions;

internal static class ExecutionTestClient
{
    public static async Task<(HttpResponseMessage Response, SendMessageResponse? Body)> SendMessageAsync(
        this HttpClient client, Guid conversationId, string content, Guid? clientRequestId = null, string? modelId = null, Guid? makeTopicId = null)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/conversations/{conversationId}/messages",
            new SendMessageRequest(content, clientRequestId ?? Guid.NewGuid(), modelId, makeTopicId),
            TestContext.Current.CancellationToken);
        var body = response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<SendMessageResponse>(JsonDefaults.Options, TestContext.Current.CancellationToken)
            : null;
        return (response, body);
    }

    /// <summary>讀完整個 SSE 串流（直到伺服器在終止事件後關閉連線）。</summary>
    public static async Task<List<SseItem<JsonElement>>> ReadEventsAsync(this HttpClient client, string url, long? lastEventId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (lastEventId is { } id)
        {
            request.Headers.Add("Last-Event-ID", id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        var items = new List<SseItem<JsonElement>>();
        await foreach (var item in SseParser.Create(stream, (_, data) => JsonDocument.Parse(data.ToArray()).RootElement.Clone()).EnumerateAsync(timeout.Token))
        {
            items.Add(item);
        }

        return items;
    }

    public static async Task<List<MessageResponse>> GetMessagesAsync(this HttpClient client, Guid conversationId) =>
        (await client.GetFromJsonAsync<List<MessageResponse>>($"/api/conversations/{conversationId}/messages", JsonDefaults.Options, TestContext.Current.CancellationToken))!;
}
