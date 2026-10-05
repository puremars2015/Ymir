using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.VibeMaker.Application.Projects;
using Ymir.VibeMaker.Contracts.Projects;

namespace Ymir.Api.Endpoints;

/// <summary>專案 API 與使用者 runtime 狀態（ADR-0007，取代 SA §9 的 Workspace API）。別人的專案一律 404（不洩漏存在與否）。</summary>
internal static class ProjectEndpoints
{
    public const string NotFoundCode = "PROJECT_NOT_FOUND";

    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/projects").WithTags("Projects").RequireAntiforgeryHeader();

        group.MapGet("/", (ProjectService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("ListProjects");

        group.MapPost("/", async (CreateProjectRequest request, ProjectService service, CancellationToken ct) =>
            {
                var project = await service.CreateAsync(request, ct);
                return TypedResults.Created($"/api/projects/{project.Id}", project);
            })
            .WithName("CreateProject");

        group.MapGet("/{projectId:guid}", async Task<IResult> (Guid projectId, ProjectService service, CancellationToken ct) =>
                await service.GetAsync(projectId, ct) is { } project ? TypedResults.Ok(project) : NotFound())
            .WithName("GetProject")
            .Produces<ProjectResponse>();

        // 一個使用者一個 runtime：只查自己的，不接受任何 id。
        endpoints.MapGet("/api/runtime", (RuntimeQueryService service, CancellationToken ct) => service.GetCurrentAsync(ct))
            .WithName("GetRuntime")
            .WithTags("Runtime");

        return endpoints;
    }

    public static IResult NotFound() => ApiProblem.Create(StatusCodes.Status404NotFound, NotFoundCode, "找不到專案。");
}
