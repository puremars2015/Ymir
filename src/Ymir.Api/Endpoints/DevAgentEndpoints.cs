using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Ymir.VibeMaker.Application.Agents;
using Ymir.VibeMaker.Application.Runtime;

namespace Ymir.Api.Endpoints;

/// <summary>
/// Sprint 0 的開發用端點：直接把 harness 事件以 SSE 串流出來，驗證「Runtime → Agent → SSE」管線。
/// 只在 Development 註冊；沒有驗證、沒有 DB，execution 綁在 HTTP request 上（正式版會改為背景執行，見開發規劃 §5）。
/// </summary>
internal static class DevAgentEndpoints
{
    /// <summary>未指定時使用的固定 workspace / session，方便連續對話測試 session 續接。</summary>
    internal static readonly Guid DefaultWorkspaceId = Guid.Parse("00000000-0000-0000-0000-00000000d001");
    internal static readonly Guid DefaultSessionId = Guid.Parse("00000000-0000-0000-0000-00000000d0a1");

    public static IEndpointRouteBuilder MapDevAgentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/dev/agent-stream", StreamAsync)
            .WithName("DevAgentStream")
            .WithSummary("（開發用）執行一次 Agent prompt，並以 SSE 串流 execution 事件。");
        return endpoints;
    }

    private static async Task<IResult> StreamAsync(
        string prompt,
        Guid? workspaceId,
        Guid? sessionId,
        IAgentRuntimeManager runtimeManager,
        IAgentHarness harness,
        HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return TypedResults.Problem("prompt is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var cancellationToken = httpContext.RequestAborted;
        var runtime = await runtimeManager.EnsureRuntimeAsync(workspaceId ?? DefaultWorkspaceId, cancellationToken);
        var request = new AgentRunRequest(Guid.NewGuid(), runtime.RuntimeId, sessionId ?? DefaultSessionId, prompt);
        return TypedResults.ServerSentEvents(StreamEventsAsync(harness, request, cancellationToken));
    }

    private static async IAsyncEnumerable<SseItem<string>> StreamEventsAsync(
        IAgentHarness harness,
        AgentRunRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long sequence = 0;
        await foreach (var agentEvent in harness.RunAsync(request, cancellationToken))
        {
            var executionEvent = agentEvent.ToExecutionEvent(request.ExecutionId);
            yield return new SseItem<string>(executionEvent.ToJson(), executionEvent.EventName)
            {
                EventId = (++sequence).ToString(CultureInfo.InvariantCulture),
            };
        }
    }
}
