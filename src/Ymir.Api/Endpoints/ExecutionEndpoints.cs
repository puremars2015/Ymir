using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.VibeMaker.Application.Executions;
using Ymir.VibeMaker.Contracts.Conversations;
using Ymir.VibeMaker.Contracts.Executions;

namespace Ymir.Api.Endpoints;

/// <summary>送出訊息、SSE 事件、取消（SA §9、§10、§11）。</summary>
internal static class ExecutionEndpoints
{
    public const string NotFoundCode = "EXECUTION_NOT_FOUND";

    public static IEndpointRouteBuilder MapExecutionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/conversations/{conversationId:guid}/messages", SendMessageAsync)
            .WithName("SendMessage")
            .WithTags("Conversations")
            .RequireAntiforgeryHeader()
            .Produces<SendMessageResponse>(StatusCodes.Status202Accepted);

        var group = endpoints.MapGroup("/api/executions").WithTags("Executions").RequireAntiforgeryHeader();

        group.MapGet("/{executionId:guid}/events", StreamEventsAsync)
            .WithName("StreamExecutionEvents")
            .WithSummary("SSE：execution 即時事件。支援 Last-Event-ID 斷線續傳。");

        group.MapPost("/{executionId:guid}/cancel", async Task<IResult> (Guid executionId, ExecutionService service, CancellationToken ct) =>
                await service.CancelAsync(executionId, ct) is { } response ? TypedResults.Accepted((string?)null, response) : NotFound())
            .WithName("CancelExecution")
            .Produces<CancelExecutionResponse>(StatusCodes.Status202Accepted);

        return endpoints;
    }

    private static async Task<IResult> SendMessageAsync(Guid conversationId, SendMessageRequest request, ExecutionService service, CancellationToken ct)
    {
        var result = await service.SubmitAsync(conversationId, request, ct);
        return result.Outcome switch
        {
            SubmitMessageOutcome.Accepted => TypedResults.Accepted(result.Response!.EventStreamUrl, result.Response),
            SubmitMessageOutcome.Conflict => ApiProblem.Create(StatusCodes.Status409Conflict, ExecutionErrorCodes.ExecutionConflict, "這個對話還有執行中的工作，請等待完成或先停止。"),
            SubmitMessageOutcome.Forbidden => ApiProblem.Create(StatusCodes.Status403Forbidden, ExecutionErrorCodes.Forbidden, "帳號已停用，無法執行 Agent。"),
            _ => ConversationEndpoints.NotFound(),
        };
    }

    private static async Task<IResult> StreamEventsAsync(
        Guid executionId,
        long? lastEventId,
        HttpContext httpContext,
        ExecutionService service,
        IExecutionEventBus bus,
        CancellationToken ct)
    {
        if (!await service.CanReadAsync(executionId, ct))
        {
            return NotFound();
        }

        // 瀏覽器 EventSource 重連時會帶 Last-Event-ID header；也接受 query string 方便首次連線指定。
        var after = long.TryParse(httpContext.Request.Headers["Last-Event-ID"], NumberStyles.None, CultureInfo.InvariantCulture, out var headerId)
            ? headerId
            : lastEventId ?? 0;
        httpContext.Response.Headers["X-Accel-Buffering"] = "no"; // 反向代理不要緩衝串流
        return TypedResults.ServerSentEvents(ReadEventsAsync(executionId, after, service, bus, ct));
    }

    /// <summary>先訂閱即時事件，再從資料庫補齊 <paramref name="after"/> 之後的事件，以 sequence 去重，直到終止事件。</summary>
    private static async IAsyncEnumerable<SseItem<string>> ReadEventsAsync(
        Guid executionId,
        long after,
        ExecutionService service,
        IExecutionEventBus bus,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var subscription = bus.Subscribe(executionId);
        var last = after;
        foreach (var stored in await service.GetEventsAfterAsync(executionId, after, ct))
        {
            yield return ToSseItem(stored);
            last = stored.Sequence;
            if (stored.IsTerminal)
            {
                yield break;
            }
        }

        await foreach (var stored in subscription.ReadAllAsync(ct))
        {
            if (stored.Sequence <= last)
            {
                continue;
            }

            yield return ToSseItem(stored);
            last = stored.Sequence;
            if (stored.IsTerminal)
            {
                yield break;
            }
        }
    }

    private static SseItem<string> ToSseItem(StoredExecutionEvent stored) =>
        new(stored.Data, stored.EventType) { EventId = stored.Sequence.ToString(CultureInfo.InvariantCulture) };

    private static IResult NotFound() => ApiProblem.Create(StatusCodes.Status404NotFound, NotFoundCode, "找不到執行紀錄。");
}
