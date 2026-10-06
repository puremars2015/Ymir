using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.Platform.Auditing;
using Ymir.Platform.Identity;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Application.Admin;
using Ymir.VibeMaker.Domain;

namespace Ymir.Api.Endpoints;

/// <summary>
/// Admin 總覽與稽核紀錄（ADR-0010）。跨模組的資料（使用者名稱 + Vibe Maker 統計）在 Api 層組合，
/// 模組之間不互相參考（ADR-0001）。
/// </summary>
internal static class AdminOverviewEndpoints
{
    /// <summary>UTC 位移的合理範圍（UTC-14 ～ UTC+14）。</summary>
    private const int MaxOffsetMinutes = 14 * 60;

    public static IEndpointRouteBuilder MapAdminOverviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin").WithTags("Admin").RequireAuthorization(AuthSetup.AdminPolicy).RequireAntiforgeryHeader();

        group.MapGet("/overview", GetOverviewAsync).WithName("AdminGetOverview").Produces<AdminOverviewResponse>();
        group.MapPost("/runtimes/{userId:guid}/stop", StopRuntimeAsync).WithName("AdminStopRuntime").Produces(StatusCodes.Status204NoContent);
        group.MapGet("/audit", SearchAuditAsync).WithName("AdminSearchAudit").Produces<AuditLogPageResponse>();
        return endpoints;
    }

    private static async Task<IResult> GetOverviewAsync(
        int? utcOffsetMinutes,
        IUserDirectory users,
        AdminStatsService stats,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var offset = utcOffsetMinutes ?? 0;
        if (Math.Abs(offset) > MaxOffsetMinutes)
        {
            return ApiProblem.Create(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", "utcOffsetMinutes 超出範圍。");
        }

        var userStats = await users.GetStatisticsAsync(timeProvider.GetUtcNow().AddDays(-1), cancellationToken);
        var executions = await stats.GetExecutionStatisticsAsync(TimeSpan.FromMinutes(offset), cancellationToken);
        var runtimes = await stats.ListRuntimesAsync(cancellationToken);
        var names = await DisplayNamesAsync(users, [.. runtimes.Select(r => r.UserId)], cancellationToken);

        return TypedResults.Ok(new AdminOverviewResponse(
            new UserCountsResponse(userStats.Total, userStats.Active, userStats.Disabled, userStats.Admins, userStats.ActiveSince),
            new ExecutionCountsResponse(
                executions.Running,
                executions.Queued,
                executions.CompletedToday,
                executions.FailedToday,
                executions.CancelledToday,
                [.. executions.Trend.Select(d => new DailyExecutionResponse(d.Date, d.Total, d.Failed))]),
            [.. runtimes.Select(r => new RuntimeSummaryResponse(r.UserId, names.GetValueOrDefault(r.UserId) ?? "（已刪除的使用者）", r.Status, r.LastActiveAt))]));
    }

    private static async Task<IResult> StopRuntimeAsync(Guid userId, AdminStatsService stats, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        await stats.StopRuntimeAsync(userId, currentUser.ActorName, cancellationToken)
            ? TypedResults.NoContent()
            : ApiProblem.Create(StatusCodes.Status404NotFound, "RUNTIME_NOT_FOUND", "這位使用者目前沒有執行環境。");

    private static async Task<IResult> SearchAuditAsync(
        string? action,
        Guid? userId,
        AuditResult? result,
        DateTimeOffset? from,
        DateTimeOffset? to,
        long? before,
        int? take,
        IAuditLogQuery audit,
        IUserDirectory users,
        CancellationToken cancellationToken)
    {
        var page = await audit.SearchAsync(new AuditLogFilter(action, userId, result, from, to, before, take ?? 50), cancellationToken);

        // actor 與目標是使用者時，換成顯示名稱，方便閱讀
        var ids = page.Items
            .SelectMany(i => new[] { AuditActor.TryGetUserId(i.Actor), i.TargetType == "user" && Guid.TryParse(i.TargetId, out var t) ? t : (Guid?)null })
            .OfType<Guid>()
            .ToList();
        var names = await DisplayNamesAsync(users, ids, cancellationToken);

        return TypedResults.Ok(new AuditLogPageResponse(
            [.. page.Items.Select(i =>
            {
                var actorId = AuditActor.TryGetUserId(i.Actor);
                var targetName = i.TargetType == "user" && Guid.TryParse(i.TargetId, out var targetId) ? names.GetValueOrDefault(targetId) : null;
                return new AuditLogItemResponse(
                    i.Id,
                    i.Timestamp,
                    i.Action,
                    i.Result,
                    actorId,
                    actorId is { } id ? names.GetValueOrDefault(id) ?? i.Actor : i.Actor,
                    i.TargetType,
                    i.TargetId,
                    targetName,
                    i.CorrelationId);
            })],
            page.NextBeforeId));
    }

    private static async Task<Dictionary<Guid, string>> DisplayNamesAsync(IUserDirectory users, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        (await users.FindManyAsync(ids, cancellationToken)).ToDictionary(u => u.Id, u => u.DisplayName);
}

public sealed record AdminOverviewResponse(UserCountsResponse Users, ExecutionCountsResponse Executions, List<RuntimeSummaryResponse> Runtimes);

/// <param name="RecentlyActive">24 小時內登入過的使用者數。</param>
public sealed record UserCountsResponse(int Total, int Active, int Disabled, int Admins, int RecentlyActive);

public sealed record ExecutionCountsResponse(int Running, int Queued, int CompletedToday, int FailedToday, int CancelledToday, List<DailyExecutionResponse> Trend);

public sealed record DailyExecutionResponse(DateOnly Date, int Total, int Failed);

public sealed record RuntimeSummaryResponse(Guid UserId, string DisplayName, RuntimeStatus Status, DateTimeOffset? LastActiveAt);

public sealed record AuditLogPageResponse(List<AuditLogItemResponse> Items, long? NextBefore);

public sealed record AuditLogItemResponse(
    long Id,
    DateTimeOffset Timestamp,
    string Action,
    AuditResult Result,
    Guid? ActorUserId,
    string ActorName,
    string TargetType,
    string TargetId,
    string? TargetName,
    string? CorrelationId);
