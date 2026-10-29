using Microsoft.EntityFrameworkCore;
using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.VibeMaker.Application.Schedules;

namespace Ymir.Api.Endpoints;

internal static class ScheduledTaskEndpoints
{
    public static void MapScheduledTaskEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/scheduled-tasks").WithTags("Schedules").RequireAntiforgeryHeader();
        group.MapGet("", (ScheduledTaskService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("ListScheduledTasks").Produces<IReadOnlyList<ScheduledTaskResponse>>();
        group.MapPost("", (SaveScheduledTask request, ScheduledTaskService service, CancellationToken ct) =>
            HandleAsync(() => service.SaveAsync(null, request, null, ct)))
            .WithName("CreateScheduledTask").Produces<ScheduledTaskResponse>();
        group.MapPut("/{id:guid}", (Guid id, UpdateScheduledTask request, ScheduledTaskService service, CancellationToken ct) =>
            HandleAsync(() => service.SaveAsync(id, request.Task, request.Version, ct)))
            .WithName("UpdateScheduledTask").Produces<ScheduledTaskResponse>();
        foreach (var action in new[] { "pause", "resume", "confirm" })
            group.MapPost("/{id:guid}/" + action, (Guid id, ScheduleVersion request, ScheduledTaskService service, CancellationToken ct) =>
                HandleAsync(() => service.ChangeAsync(id, action, request.Version, ct)))
                .WithName(action + "ScheduledTask").Produces<ScheduledTaskResponse>();
        group.MapDelete("/{id:guid}", (Guid id, string version, ScheduledTaskService service, CancellationToken ct) =>
            HandleAsync(() => service.ChangeAsync(id, "delete", version, ct)))
            .WithName("DeleteScheduledTask").Produces<ScheduledTaskResponse>();
    }
    private static async Task<IResult> HandleAsync(Func<Task<ScheduledTaskResponse?>> operation)
    {
        try
        {
            return await operation() is { } task ? TypedResults.Ok(task)
                : ApiProblem.Create(404, "SCHEDULE_NOT_FOUND", "找不到排程或對話。");
        }
        catch (DbUpdateConcurrencyException) { return ApiProblem.Create(409, "SCHEDULE_CONFLICT", "排程已更新或正在觸發，請重新整理。"); }
    }
}

public sealed record UpdateScheduledTask(SaveScheduledTask Task, string Version);
public sealed record ScheduleVersion(string Version);
