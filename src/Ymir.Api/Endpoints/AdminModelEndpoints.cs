using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.Platform.Auditing;
using Ymir.Platform.Identity;
using Ymir.VibeMaker.Application.Models;
using Ymir.VibeMaker.Contracts.Models;

namespace Ymir.Api.Endpoints;

internal static class AdminModelEndpoints
{
    public static IEndpointRouteBuilder MapAdminModelEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/settings/models").WithTags("Admin").RequireAuthorization(AuthSetup.AdminPolicy).RequireAntiforgeryHeader();
        group.MapGet("/", async (ModelAccessService policy, CancellationToken ct) => ToResponse(policy, await policy.GetAsync(ct))).WithName("AdminGetModelAccess");
        group.MapPut("/", SaveAsync).WithName("AdminSaveModelAccess").Produces<ModelAccessResponse>();
        group.MapDelete("/", ResetAsync).WithName("AdminResetModelAccess").Produces<ModelAccessResponse>();
        return endpoints;
    }

    private static ModelAccessResponse ToResponse(ModelAccessService policy, ModelAccessState state) => new(
        policy.DeploymentModels.Select(m => new AdminModelOptionResponse(m.Id, m.DisplayName, m.SupportsImages, state.IsAvailable(m.Id))).ToList(),
        state.DefaultModelId, state.Stored is null, state.Stored?.UpdatedAt);

    private static async Task<IResult> SaveAsync(SaveModelAccessRequest request, ModelAccessService policy, ICurrentUser user, IAuditLog audit, TimeProvider time, CancellationToken ct)
    {
        var settings = new ModelAccessSettings(request.EnabledModelIds, request.DefaultModelId);
        if (policy.Validate(settings) is { } problem) return ApiProblem.Create(400, "VALIDATION_FAILED", problem);
        var state = await policy.SaveAsync(settings, user.ActorName, ct);
        await audit.WriteAsync(new AuditEntry(user.ActorName, "admin.settings.models.update", "setting", ModelAccessService.Key, AuditResult.Success, time.GetUtcNow(), null), ct);
        return TypedResults.Ok(ToResponse(policy, state));
    }

    private static async Task<ModelAccessResponse> ResetAsync(ModelAccessService policy, ICurrentUser user, IAuditLog audit, TimeProvider time, CancellationToken ct)
    {
        var state = await policy.ResetAsync(ct);
        await audit.WriteAsync(new AuditEntry(user.ActorName, "admin.settings.models.reset", "setting", ModelAccessService.Key, AuditResult.Success, time.GetUtcNow(), null), ct);
        return ToResponse(policy, state);
    }
}
