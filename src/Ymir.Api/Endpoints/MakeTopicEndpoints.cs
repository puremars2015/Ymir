using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.VibeMaker.Application.Make;
using Ymir.VibeMaker.Contracts.Make;

namespace Ymir.Api.Endpoints;

/// <summary>
/// <c>/make</c> 主題：已登入的使用者可以讀取啟用中的主題（按鈕）；Admin 可以新增 / 修改 / 刪除（含給 Agent 的建置指示）。
/// 欄位驗證失敗由 <c>DomainExceptionHandler</c> 轉成 400 VALIDATION_FAILED。
/// </summary>
internal static class MakeTopicEndpoints
{
    public static IEndpointRouteBuilder MapMakeTopicEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/make-topics", (MakeTopicService service, CancellationToken ct) => service.ListEnabledAsync(ct))
            .WithName("ListMakeTopics")
            .WithTags("Make");

        var admin = endpoints.MapGroup("/api/admin/make-topics").WithTags("Admin").RequireAuthorization(AuthSetup.AdminPolicy).RequireAntiforgeryHeader();

        admin.MapGet("/", (MakeTopicService service, CancellationToken ct) => service.ListAllAsync(ct))
            .WithName("AdminListMakeTopics");

        admin.MapPost("/", async (SaveMakeTopicRequest request, MakeTopicService service, CancellationToken ct) =>
            {
                var topic = await service.CreateAsync(request, ct);
                return TypedResults.Created($"/api/admin/make-topics/{topic.Id}", topic);
            })
            .WithName("AdminCreateMakeTopic");

        admin.MapPut("/{topicId:guid}", async Task<IResult> (Guid topicId, SaveMakeTopicRequest request, MakeTopicService service, CancellationToken ct) =>
                await service.UpdateAsync(topicId, request, ct) is { } topic ? TypedResults.Ok(topic) : TopicNotFound())
            .WithName("AdminUpdateMakeTopic")
            .Produces<AdminMakeTopicResponse>();

        admin.MapDelete("/{topicId:guid}", async Task<IResult> (Guid topicId, MakeTopicService service, CancellationToken ct) =>
                await service.DeleteAsync(topicId, ct) ? TypedResults.NoContent() : TopicNotFound())
            .WithName("AdminDeleteMakeTopic")
            .Produces(StatusCodes.Status204NoContent);

        return endpoints;
    }

    private static IResult TopicNotFound() => ApiProblem.Create(StatusCodes.Status404NotFound, "MAKE_TOPIC_NOT_FOUND", "找不到這個主題。");
}
