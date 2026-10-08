using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.Platform.Auditing;
using Ymir.Platform.Identity;
using Ymir.Platform.Users;
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
        var users = endpoints.MapGroup("/api/admin/users/{userId:guid}/models").WithTags("Admin").RequireAuthorization(AuthSetup.AdminPolicy).RequireAntiforgeryHeader();
        users.MapGet("/", GetUserAsync).WithName("AdminGetUserModelAccess").Produces<UserModelAccessResponse>();
        users.MapPut("/", SaveUserAsync).WithName("AdminSaveUserModelAccess").Produces<UserModelAccessResponse>();
        users.MapDelete("/", ResetUserAsync).WithName("AdminResetUserModelAccess").Produces<UserModelAccessResponse>();
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

    private static UserModelAccessResponse ToUserResponse(ModelAccessService policy, UserModelAccessState state) => new(
        policy.DeploymentModels.Select(m => new UserModelOptionResponse(m.Id, m.DisplayName, state.System.IsAvailable(m.Id),
            state.Overrides.TryGetValue(m.Id, out var value) ? value : null, state.Effective.IsAvailable(m.Id))).ToList(),
        state.Effective.DefaultModelId, state.IsValid, state.Effective.Stored?.UpdatedAt);

    private static IResult UserNotFound() => ApiProblem.Create(404, "USER_NOT_FOUND", "找不到使用者。");

    private static async Task<IResult> GetUserAsync(Guid userId, ModelAccessService policy, IUserDirectory users, CancellationToken ct)
    {
        if (await users.FindAsync(userId, ct) is null) return UserNotFound();
        return TypedResults.Ok(ToUserResponse(policy, await policy.GetUserStateAsync(userId, ct)));
    }

    private static async Task<IResult> SaveUserAsync(Guid userId, SaveUserModelAccessRequest request, ModelAccessService policy, IUserDirectory users,
        ICurrentUser user, IAuditLog audit, TimeProvider time, CancellationToken ct)
    {
        if (await users.FindAsync(userId, ct) is null) return UserNotFound();
        if (policy.ValidateUserOverrides(request.Overrides) is { } problem) return ApiProblem.Create(400, "VALIDATION_FAILED", problem);
        var state = await policy.SaveUserOverridesAsync(userId, request.Overrides, user.ActorName, ct);
        await audit.WriteAsync(new AuditEntry(user.ActorName, "admin.user.models.update", "user", userId.ToString("D"), AuditResult.Success, time.GetUtcNow(), null), ct);
        return TypedResults.Ok(ToUserResponse(policy, state));
    }

    private static async Task<IResult> ResetUserAsync(Guid userId, ModelAccessService policy, IUserDirectory users,
        ICurrentUser user, IAuditLog audit, TimeProvider time, CancellationToken ct)
    {
        if (await users.FindAsync(userId, ct) is null) return UserNotFound();
        var state = await policy.ResetUserOverridesAsync(userId, ct);
        await audit.WriteAsync(new AuditEntry(user.ActorName, "admin.user.models.reset", "user", userId.ToString("D"), AuditResult.Success, time.GetUtcNow(), null), ct);
        return TypedResults.Ok(ToUserResponse(policy, state));
    }
}
