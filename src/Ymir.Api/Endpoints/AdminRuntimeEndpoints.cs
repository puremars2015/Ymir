using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.Platform.Auditing;
using Ymir.Platform.Identity;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Application.Admin;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Domain;

namespace Ymir.Api.Endpoints;

/// <summary>
/// Admin：執行政策（閒置停止、單次執行上限、每人配額，ADR-0011）與各使用者的用量。
/// 政策存在 <c>platform.system_settings</c>，優先於部署設定，存檔後不必重新啟動。
/// </summary>
internal static class AdminRuntimeEndpoints
{
    public const int MaxUsageDays = 90;

    public static IEndpointRouteBuilder MapAdminRuntimeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin").WithTags("Admin").RequireAuthorization(AuthSetup.AdminPolicy).RequireAntiforgeryHeader();

        group.MapGet("/settings/runtime", GetPolicyAsync).WithName("AdminGetRuntimePolicy").Produces<RuntimePolicyResponse>();
        group.MapPut("/settings/runtime", SavePolicyAsync).WithName("AdminSaveRuntimePolicy").Produces<RuntimePolicyResponse>();
        group.MapDelete("/settings/runtime", ResetPolicyAsync).WithName("AdminResetRuntimePolicy").Produces<RuntimePolicyResponse>();
        group.MapGet("/usage", GetUsageAsync).WithName("AdminGetUsage").Produces<AdminUsageResponse>();
        return endpoints;
    }

    private static async Task<RuntimePolicyResponse> GetPolicyAsync(RuntimePolicyService policies, IUserDirectory users, CancellationToken cancellationToken) =>
        await ToResponseAsync(await policies.RefreshAsync(cancellationToken), users, cancellationToken);

    private static async Task<IResult> SavePolicyAsync(
        SaveRuntimePolicyRequest request,
        RuntimePolicyService policies,
        IUserDirectory users,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var settings = new RuntimePolicySettings(request.IdleTimeoutMinutes, request.ExecutionTimeoutMinutes, request.MaxPendingExecutionsPerUser, request.DailyExecutionLimit);
        if (settings.Validate() is { } problem)
        {
            return ApiProblem.Create(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", problem);
        }

        var state = await policies.SaveAsync(settings, currentUser.ActorName, cancellationToken);
        await auditLog.WriteAsync(
            new AuditEntry(currentUser.ActorName, "admin.settings.runtime.update", "setting", RuntimePolicyService.Key, AuditResult.Success, timeProvider.GetUtcNow(), null),
            cancellationToken);
        return TypedResults.Ok(await ToResponseAsync(state, users, cancellationToken));
    }

    private static async Task<RuntimePolicyResponse> ResetPolicyAsync(
        RuntimePolicyService policies,
        IUserDirectory users,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var state = await policies.ResetAsync(cancellationToken);
        await auditLog.WriteAsync(
            new AuditEntry(currentUser.ActorName, "admin.settings.runtime.reset", "setting", RuntimePolicyService.Key, AuditResult.Success, timeProvider.GetUtcNow(), null),
            cancellationToken);
        return await ToResponseAsync(state, users, cancellationToken);
    }

    private static async Task<IResult> GetUsageAsync(
        int? days,
        AdminStatsService stats,
        RuntimePolicyService policies,
        IUserDirectory users,
        CancellationToken cancellationToken)
    {
        var range = days ?? 7;
        if (range is < 1 or > MaxUsageDays)
        {
            return ApiProblem.Create(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", $"days 必須在 1～{MaxUsageDays} 之間。");
        }

        var usage = await stats.GetUsageAsync(range, cancellationToken);
        var policy = await policies.GetAsync(cancellationToken);
        var names = new Dictionary<Guid, string>();
        foreach (var userId in usage.Select(u => u.UserId).Distinct())
        {
            if (await users.FindAsync(userId, cancellationToken) is { } user)
            {
                names[userId] = user.DisplayName;
            }
        }

        return TypedResults.Ok(new AdminUsageResponse(
            range,
            policy.DailyExecutionLimit,
            [.. usage.Select(u => new UserUsageResponse(
                u.UserId,
                names.GetValueOrDefault(u.UserId) ?? "（已刪除的使用者）",
                u.Executions,
                u.Completed,
                u.Failed,
                u.Cancelled,
                Math.Round(u.RunTime.TotalMinutes, 1),
                u.Last24Hours,
                u.LastExecutionAt,
                u.RuntimeStatus))]));
    }

    private static async Task<RuntimePolicyResponse> ToResponseAsync(RuntimePolicyState state, IUserDirectory users, CancellationToken cancellationToken)
    {
        var updatedByName = state.Stored?.UpdatedBy is { } actor && AuditActor.TryGetUserId(actor) is { } userId
            ? (await users.FindAsync(userId, cancellationToken))?.DisplayName
            : null;
        return new RuntimePolicyResponse(
            ToValues(state.Effective),
            ToValues(state.Deployment),
            state.Stored is null ? OidcSettingsSource.Deployment : OidcSettingsSource.Database,
            state.Stored?.UpdatedAt,
            updatedByName);
    }

    private static RuntimePolicyValues ToValues(RuntimePolicy policy) => new(
        policy.IdleTimeout.TotalMinutes,
        policy.ExecutionTimeout.TotalMinutes,
        policy.MaxPendingExecutionsPerUser,
        policy.DailyExecutionLimit);
}

/// <param name="IdleTimeoutMinutes">0 表示不自動停止。</param>
/// <param name="DailyExecutionLimit">0 表示不限制。</param>
public sealed record SaveRuntimePolicyRequest(int IdleTimeoutMinutes, int ExecutionTimeoutMinutes, int MaxPendingExecutionsPerUser, int DailyExecutionLimit);

/// <param name="IdleTimeoutMinutes">0 表示不自動停止。</param>
/// <param name="DailyExecutionLimit">0 表示不限制。</param>
public sealed record RuntimePolicyValues(double IdleTimeoutMinutes, double ExecutionTimeoutMinutes, int MaxPendingExecutionsPerUser, int DailyExecutionLimit);

/// <param name="Effective">目前生效的值。</param>
/// <param name="Deployment">部署設定（.env）的值；還原後使用。</param>
public sealed record RuntimePolicyResponse(
    RuntimePolicyValues Effective,
    RuntimePolicyValues Deployment,
    OidcSettingsSource Source,
    DateTimeOffset? UpdatedAt,
    string? UpdatedByName);

/// <param name="DailyExecutionLimit">目前的每日上限（0 表示不限制），用來標示接近上限的使用者。</param>
public sealed record AdminUsageResponse(int Days, int DailyExecutionLimit, IReadOnlyList<UserUsageResponse> Users);

/// <param name="RunMinutes">Agent 實際執行時間（開始到結束）的總和。</param>
/// <param name="Last24Hours">過去 24 小時的執行數（與每日上限比較）。</param>
public sealed record UserUsageResponse(
    Guid UserId,
    string DisplayName,
    int Executions,
    int Completed,
    int Failed,
    int Cancelled,
    double RunMinutes,
    int Last24Hours,
    DateTimeOffset? LastExecutionAt,
    RuntimeStatus? RuntimeStatus);
