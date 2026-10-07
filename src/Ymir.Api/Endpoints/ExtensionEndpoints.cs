using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.Platform.Auditing;
using Ymir.Platform.Identity;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Application.Extensions;
using Ymir.VibeMaker.Contracts.Extensions;
using Ymir.VibeMaker.Domain;

namespace Ymir.Api.Endpoints;

/// <summary>
/// Agent 擴充能力（ADR-0012 A.2、A.6、C）：
/// 成員只能看自己的有效能力與自建擴充（不接受任何 user id 或路徑）；管理員設定全域預設與每人覆寫，變更都寫稽核。
/// 政策在啟動 Agent 時由伺服器強制（<c>PiAgentHarness</c>），這裡只負責設定與檢視。
/// </summary>
internal static class ExtensionEndpoints
{
    public static IEndpointRouteBuilder MapExtensionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/extensions", GetMineAsync).WithName("GetMyExtensions").WithTags("Extensions").RequireAntiforgeryHeader();

        var admin = endpoints.MapGroup("/api/admin").WithTags("Admin").RequireAuthorization(AuthSetup.AdminPolicy).RequireAntiforgeryHeader();
        admin.MapGet("/settings/extensions", GetPolicyAsync).WithName("AdminGetExtensionPolicy").Produces<ExtensionPolicyResponse>();
        admin.MapPut("/settings/extensions", SavePolicyAsync).WithName("AdminSaveExtensionPolicy").Produces<ExtensionPolicyResponse>();
        admin.MapGet("/users/{userId:guid}/extensions", GetUserAsync).WithName("AdminGetUserExtensions").Produces<UserExtensionsResponse>();
        admin.MapPut("/users/{userId:guid}/extensions", SaveUserAsync).WithName("AdminSaveUserExtensions").Produces<UserExtensionsResponse>();
        return endpoints;
    }

    private static async Task<MyExtensionsResponse> GetMineAsync(
        ICurrentUser currentUser,
        ExtensionPolicyService policy,
        IExtensionInventory inventory,
        CancellationToken cancellationToken)
    {
        var effective = await policy.ResolveAsync(currentUser.UserId, cancellationToken);
        var items = await inventory.ListAsync(currentUser.UserId, cancellationToken);
        return new MyExtensionsResponse(effective.Skills, effective.Mcp, items is not null, items?.Skills ?? [], items?.McpServers ?? []);
    }

    private static async Task<ExtensionPolicyResponse> GetPolicyAsync(ExtensionPolicyService policy, IUserDirectory users, CancellationToken cancellationToken) =>
        await ToResponseAsync(await policy.RefreshAsync(cancellationToken), users, cancellationToken);

    private static async Task<ExtensionPolicyResponse> SavePolicyAsync(
        SaveExtensionPolicyRequest request,
        ExtensionPolicyService policy,
        IUserDirectory users,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var state = await policy.SaveAsync(new ExtensionPolicySettings(request.Skills, request.Mcp), currentUser.ActorName, cancellationToken);
        await auditLog.WriteAsync(
            new AuditEntry(currentUser.ActorName, "admin.settings.extensions.update", "setting", ExtensionPolicyService.Key, AuditResult.Success, timeProvider.GetUtcNow(), null),
            cancellationToken);
        return await ToResponseAsync(state, users, cancellationToken);
    }

    private static async Task<IResult> GetUserAsync(Guid userId, ExtensionPolicyService policy, IUserDirectory users, CancellationToken cancellationToken)
    {
        if (await users.FindAsync(userId, cancellationToken) is null)
        {
            return UserNotFound();
        }

        return TypedResults.Ok(ToResponse(await policy.GetUserStateAsync(userId, cancellationToken)));
    }

    private static async Task<IResult> SaveUserAsync(
        Guid userId,
        SaveUserExtensionsRequest request,
        ExtensionPolicyService policy,
        IUserDirectory users,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (await users.FindAsync(userId, cancellationToken) is null)
        {
            return UserNotFound();
        }

        var state = await policy.SetUserGrantsAsync(
            userId,
            new Dictionary<ExtensionCapability, ExtensionGrantEffect?>
            {
                [ExtensionCapability.Skills] = ToEffect(request.Skills),
                [ExtensionCapability.Mcp] = ToEffect(request.Mcp),
            },
            currentUser.ActorName,
            cancellationToken);
        await auditLog.WriteAsync(
            new AuditEntry(currentUser.ActorName, "admin.user.extensions.update", "user", userId.ToString("D"), AuditResult.Success, timeProvider.GetUtcNow(), null),
            cancellationToken);
        return TypedResults.Ok(ToResponse(state));
    }

    private static ExtensionGrantEffect? ToEffect(ExtensionGrantSetting setting) => setting switch
    {
        ExtensionGrantSetting.Allow => ExtensionGrantEffect.Allow,
        ExtensionGrantSetting.Deny => ExtensionGrantEffect.Deny,
        _ => null,
    };

    private static ExtensionGrantSetting ToSetting(UserExtensionState state, ExtensionCapability capability) =>
        state.Grants.TryGetValue(capability, out var effect)
            ? effect == ExtensionGrantEffect.Allow ? ExtensionGrantSetting.Allow : ExtensionGrantSetting.Deny
            : ExtensionGrantSetting.Inherit;

    private static UserExtensionsResponse ToResponse(UserExtensionState state) => new(
        ToSetting(state, ExtensionCapability.Skills),
        ToSetting(state, ExtensionCapability.Mcp),
        new ExtensionValues(state.Effective.Skills, state.Effective.Mcp));

    private static async Task<ExtensionPolicyResponse> ToResponseAsync(ExtensionPolicyState state, IUserDirectory users, CancellationToken cancellationToken)
    {
        var updatedByName = state.Stored?.UpdatedBy is { } actor && AuditActor.TryGetUserId(actor) is { } userId
            ? (await users.FindAsync(userId, cancellationToken))?.DisplayName
            : null;
        return new ExtensionPolicyResponse(new ExtensionValues(state.Effective.Skills, state.Effective.Mcp), state.Stored?.UpdatedAt, updatedByName);
    }

    private static IResult UserNotFound() => ApiProblem.Create(StatusCodes.Status404NotFound, "USER_NOT_FOUND", "找不到使用者。");
}

/// <summary>每人覆寫：繼承全域預設、允許、禁止。</summary>
public enum ExtensionGrantSetting
{
    Inherit,
    Allow,
    Deny,
}

public sealed record ExtensionValues(bool Skills, bool Mcp);

public sealed record SaveExtensionPolicyRequest(bool Skills, bool Mcp);

/// <param name="Defaults">全域預設（沒有個人覆寫的成員套用這個值）。沒有設定過時全部關閉。</param>
public sealed record ExtensionPolicyResponse(ExtensionValues Defaults, DateTimeOffset? UpdatedAt, string? UpdatedByName);

public sealed record SaveUserExtensionsRequest(ExtensionGrantSetting Skills, ExtensionGrantSetting Mcp);

/// <param name="Effective">套用全域預設與覆寫後的結果。</param>
public sealed record UserExtensionsResponse(ExtensionGrantSetting Skills, ExtensionGrantSetting Mcp, ExtensionValues Effective);
