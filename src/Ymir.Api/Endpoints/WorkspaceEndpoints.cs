using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.VibeMaker.Application.Workspaces;
using Ymir.VibeMaker.Contracts.Workspaces;

namespace Ymir.Api.Endpoints;

/// <summary>SA §9 Workspace API。別人的 workspace 一律 404（不洩漏存在與否）。</summary>
internal static class WorkspaceEndpoints
{
    public const string NotFoundCode = "WORKSPACE_NOT_FOUND";

    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces").WithTags("Workspaces").RequireAntiforgeryHeader();

        group.MapGet("/", (WorkspaceService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("ListWorkspaces");

        group.MapPost("/", async (CreateWorkspaceRequest request, WorkspaceService service, CancellationToken ct) =>
            {
                var workspace = await service.CreateAsync(request, ct);
                return TypedResults.Created($"/api/workspaces/{workspace.Id}", workspace);
            })
            .WithName("CreateWorkspace");

        group.MapGet("/{workspaceId:guid}", async Task<IResult> (Guid workspaceId, WorkspaceService service, CancellationToken ct) =>
                await service.GetAsync(workspaceId, ct) is { } workspace ? TypedResults.Ok(workspace) : NotFound())
            .WithName("GetWorkspace")
            .Produces<WorkspaceResponse>();

        group.MapGet("/{workspaceId:guid}/runtime", async Task<IResult> (Guid workspaceId, WorkspaceService service, CancellationToken ct) =>
                await service.GetRuntimeAsync(workspaceId, ct) is { } runtime ? TypedResults.Ok(runtime) : NotFound())
            .WithName("GetWorkspaceRuntime")
            .Produces<RuntimeStatusResponse>();

        return endpoints;
    }

    private static IResult NotFound() => ApiProblem.Create(StatusCodes.Status404NotFound, NotFoundCode, "找不到 Workspace。");
}
