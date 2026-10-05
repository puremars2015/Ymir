using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.VibeMaker.Application.Conversations;
using Ymir.VibeMaker.Contracts.Conversations;
using Ymir.VibeMaker.Contracts.Executions;

namespace Ymir.Api.Endpoints;

/// <summary>SA §9 Conversation / Message API。別人的資料一律 404 <c>CONVERSATION_NOT_FOUND</c>（SA §13）。</summary>
internal static class ConversationEndpoints
{
    public static IEndpointRouteBuilder MapConversationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/conversations").WithTags("Conversations").RequireAntiforgeryHeader();

        group.MapGet("/", async Task<IResult> (Guid? projectId, ConversationService service, CancellationToken ct) =>
                await service.ListAsync(projectId, ct) is { } conversations
                    ? TypedResults.Ok(conversations)
                    : ProjectEndpoints.NotFound())
            .WithName("ListConversations")
            .Produces<IReadOnlyList<ConversationResponse>>();

        group.MapPost("/", async Task<IResult> (CreateConversationRequest request, ConversationService service, CancellationToken ct) =>
                await service.CreateAsync(request, ct) is { } conversation
                    ? TypedResults.Created($"/api/conversations/{conversation.Id}", conversation)
                    : ProjectEndpoints.NotFound())
            .WithName("CreateConversation")
            .Produces<ConversationResponse>(StatusCodes.Status201Created);

        group.MapGet("/{conversationId:guid}", async Task<IResult> (Guid conversationId, ConversationService service, CancellationToken ct) =>
                await service.GetAsync(conversationId, ct) is { } conversation ? TypedResults.Ok(conversation) : NotFound())
            .WithName("GetConversation")
            .Produces<ConversationResponse>();

        group.MapGet("/{conversationId:guid}/messages", async Task<IResult> (Guid conversationId, ConversationService service, CancellationToken ct) =>
                await service.GetMessagesAsync(conversationId, ct) is { } messages ? TypedResults.Ok(messages) : NotFound())
            .WithName("ListMessages")
            .Produces<IReadOnlyList<MessageResponse>>();

        return endpoints;
    }

    internal static IResult NotFound() =>
        ApiProblem.Create(StatusCodes.Status404NotFound, ExecutionErrorCodes.ConversationNotFound, "找不到對話。");
}
