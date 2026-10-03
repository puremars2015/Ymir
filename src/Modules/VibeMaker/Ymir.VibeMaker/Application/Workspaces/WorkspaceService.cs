using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Auditing;
using Ymir.Platform.Identity;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Contracts.Workspaces;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Workspaces;

/// <summary>
/// Workspace 用例。所有查詢都限定目前使用者（SA §12）；別人的 workspace 視同不存在（回傳 null → 404），不洩漏存在與否。
/// </summary>
public sealed class WorkspaceService(IVibeMakerDbContext db, ICurrentUser currentUser, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<WorkspaceResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var workspaces = await db.Workspaces.AsNoTracking()
            .Where(w => w.UserId == userId && w.Status == WorkspaceStatus.Active)
            .OrderByDescending(w => w.UpdatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return workspaces.Select(ToResponse).ToList();
    }

    public async Task<WorkspaceResponse> CreateAsync(CreateWorkspaceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = timeProvider.GetUtcNow();
        var workspace = Workspace.Create(currentUser.UserId, request.Name, now);
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditLog.WriteAsync(new AuditEntry(currentUser.ActorName, "workspace.create", "workspace", workspace.Id.ToString("D"), AuditResult.Success, now, null), cancellationToken)
            .ConfigureAwait(false);
        return ToResponse(workspace);
    }

    public async Task<WorkspaceResponse?> GetAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var workspace = await FindOwnedAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return workspace is null ? null : ToResponse(workspace);
    }

    public async Task<RuntimeStatusResponse?> GetRuntimeAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        if (await FindOwnedAsync(workspaceId, cancellationToken).ConfigureAwait(false) is null)
        {
            return null;
        }

        var runtime = await db.AgentRuntimes.AsNoTracking()
            .Where(r => r.WorkspaceId == workspaceId && r.Status != RuntimeStatus.Deleted)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return runtime is null
            ? new RuntimeStatusResponse(workspaceId, SaValues.Of(RuntimeStatus.NotCreated), null, null, null)
            : new RuntimeStatusResponse(workspaceId, SaValues.Of(runtime.Status), runtime.Provider, runtime.ImageVersion, runtime.LastActiveAt);
    }

    internal Task<Workspace?> FindOwnedAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        return db.Workspaces.AsNoTracking().SingleOrDefaultAsync(w => w.Id == workspaceId && w.UserId == userId, cancellationToken);
    }

    private static WorkspaceResponse ToResponse(Workspace w) => new(w.Id, w.Name, SaValues.Of(w.Status), w.CreatedAt, w.UpdatedAt);
}
