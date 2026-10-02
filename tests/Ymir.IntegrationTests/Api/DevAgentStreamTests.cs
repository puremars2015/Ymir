using System.Net;
using Ymir.VibeMaker.Contracts.Executions;

namespace Ymir.IntegrationTests.Api;

public class DevAgentStreamTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Stream_EmitsSaEventContractInOrder()
    {
        using var client = factory.CreateClient();

        var items = await SseTestClient.ReadAllAsync(client, "/api/dev/agent-stream?prompt=hello", TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionEventNames.ExecutionStarted, items[0].EventType);
        Assert.Equal(ExecutionEventNames.ExecutionCompleted, items[^1].EventType);
        Assert.Contains(items, i => i.EventType == ExecutionEventNames.AssistantDelta && i.Data.GetProperty("text").GetString() == "hello");
        Assert.Contains(items, i => i.EventType == ExecutionEventNames.ToolStarted && i.Data.GetProperty("summary").GetString() == "ls -la");

        // id 必須遞增，供之後的 Last-Event-ID 續傳使用。
        Assert.Equal(Enumerable.Range(1, items.Count).Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)), items.Select(i => i.EventId));

        var executionId = items[0].Data.GetProperty("executionId").GetGuid();
        Assert.Equal(executionId, items[^1].Data.GetProperty("executionId").GetGuid());
    }

    [Fact]
    public async Task Stream_WithoutPrompt_Returns400()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync(new Uri("/api/dev/agent-stream?prompt=", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
