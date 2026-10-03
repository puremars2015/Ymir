using System.Net.ServerSentEvents;
using System.Text.Json;

namespace Ymir.IntegrationTests.Api;

internal static class SseTestClient
{
    public static async Task<List<SseItem<JsonElement>>> ReadAllAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri(url, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var items = new List<SseItem<JsonElement>>();
        var parser = SseParser.Create(stream, (_, data) => JsonDocument.Parse(data.ToArray()).RootElement.Clone());
        await foreach (var item in parser.EnumerateAsync(cancellationToken))
        {
            items.Add(item);
        }

        return items;
    }
}
