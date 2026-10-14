using Ymir.Api.Auth;
using Ymir.Api.Maintenance;
using Ymir.Api.Problems;
using Ymir.Maintenance;
using Ymir.Platform.Auditing;
using Ymir.Platform.Identity;
using Ymir.Platform.Settings;
using Ymir.Platform.Users;

namespace Ymir.Api.Endpoints;

/// <summary>
/// Admin「系統資源」（ADR-0025）：主機 CPU / 記憶體 / 磁碟、Ymir 的 container 與 image、自動回收政策、回收紀錄與 dry run。
/// 資料來自主機維護服務（經 Unix socket，API 不掛載 container runtime socket）；只回摘要，不含 host 路徑、環境變數、掛載細節或 CLI 輸出。
/// 沒有設定維護服務的部署（Windows Docker / Local 開發）回 <c>Supported = false</c>。
/// </summary>
internal static class AdminResourceEndpoints
{
    public static IEndpointRouteBuilder MapAdminResourceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/resources").WithTags("Admin").RequireAuthorization(AuthSetup.AdminPolicy).RequireAntiforgeryHeader();

        group.MapGet("/", GetResourcesAsync).WithName("AdminGetResources").Produces<AdminResourcesResponse>();
        group.MapGet("/policy", GetPolicyAsync).WithName("AdminGetReclamationPolicy").Produces<ReclamationPolicyResponse>();
        group.MapPut("/policy", SavePolicyAsync).WithName("AdminSaveReclamationPolicy").Produces<ReclamationPolicyResponse>();
        group.MapGet("/runs", GetRunsAsync).WithName("AdminGetReclaimRuns").Produces<AdminReclaimRunsResponse>();
        group.MapPost("/dry-run", DryRunAsync).WithName("AdminReclaimDryRun").Produces<AdminDryRunResponse>();
        return endpoints;
    }

    private static async Task<AdminResourcesResponse> GetResourcesAsync(IMaintenanceClient client, CancellationToken cancellationToken)
    {
        if (!client.IsConfigured)
        {
            return new AdminResourcesResponse(false, false, null);
        }

        var snapshot = await client.GetResourcesAsync(cancellationToken);
        return new AdminResourcesResponse(true, snapshot is not null, snapshot);
    }

    private static async Task<ReclamationPolicyResponse> GetPolicyAsync(ISystemSettingsStore store, IMaintenanceClient client, IUserDirectory users, CancellationToken cancellationToken) =>
        await ToResponseAsync(await ReclamationPolicyStore.GetAsync(store, cancellationToken), client.IsConfigured, synced: null, users, cancellationToken);

    private static async Task<IResult> SavePolicyAsync(
        ReclamationPolicy request,
        ISystemSettingsStore store,
        IMaintenanceClient client,
        IUserDirectory users,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (request.Validate() is { } problem)
        {
            return ApiProblem.Create(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", problem);
        }

        await ReclamationPolicyStore.SaveAsync(store, request, currentUser.ActorName, cancellationToken);
        await auditLog.WriteAsync(
            new AuditEntry(currentUser.ActorName, "admin.settings.reclamation.update", "setting", ReclamationPolicyStore.Key, AuditResult.Success, timeProvider.GetUtcNow(), null),
            cancellationToken);

        // 立即同步；失敗時由背景工作每分鐘重試（服務沿用上一次的政策）。
        var synced = client.IsConfigured ? await client.SetPolicyAsync(request, cancellationToken) : (bool?)null;
        return TypedResults.Ok(await ToResponseAsync(await ReclamationPolicyStore.GetAsync(store, cancellationToken), client.IsConfigured, synced, users, cancellationToken));
    }

    private static async Task<AdminReclaimRunsResponse> GetRunsAsync(IMaintenanceClient client, CancellationToken cancellationToken)
    {
        if (!client.IsConfigured)
        {
            return new AdminReclaimRunsResponse(false, false, []);
        }

        var runs = await client.GetRunsAsync(0, cancellationToken);
        return new AdminReclaimRunsResponse(true, runs is not null, runs is null ? [] : [.. runs.OrderByDescending(r => r.Id)]);
    }

    private static async Task<AdminDryRunResponse> DryRunAsync(IMaintenanceClient client, CancellationToken cancellationToken)
    {
        if (!client.IsConfigured)
        {
            return new AdminDryRunResponse(false, false, null);
        }

        var result = await client.DryRunAsync(cancellationToken);
        return new AdminDryRunResponse(true, result is not null, result);
    }

    private static async Task<ReclamationPolicyResponse> ToResponseAsync(StoredReclamationPolicy state, bool supported, bool? synced, IUserDirectory users, CancellationToken cancellationToken)
    {
        var updatedByName = state.Stored?.UpdatedBy is { } actor && AuditActor.TryGetUserId(actor) is { } userId
            ? (await users.FindAsync(userId, cancellationToken))?.DisplayName
            : null;
        return new ReclamationPolicyResponse(state.Policy, supported, state.Stored?.UpdatedAt, updatedByName, synced);
    }
}

/// <param name="Supported">這個部署是否設定了主機維護服務。</param>
/// <param name="Available">本次查詢是否成功；false 時不顯示任何數值（不可當作零）。</param>
public sealed record AdminResourcesResponse(bool Supported, bool Available, ResourceSnapshot? Snapshot);

/// <param name="Synced">儲存後是否已同步到維護服務；null 表示沒有同步（查詢或不支援）。</param>
public sealed record ReclamationPolicyResponse(ReclamationPolicy Policy, bool Supported, DateTimeOffset? UpdatedAt, string? UpdatedByName, bool? Synced);

public sealed record AdminReclaimRunsResponse(bool Supported, bool Available, IReadOnlyList<ReclaimRun> Runs);

public sealed record AdminDryRunResponse(bool Supported, bool Available, DryRunResult? Result);
