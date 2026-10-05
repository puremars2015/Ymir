using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Auditing;
using Ymir.Platform.Identity;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Contracts.Projects;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Projects;

/// <summary>
/// 專案用例（ADR-0007）。所有查詢都限定目前使用者（SA §12）；別人的專案視同不存在（回傳 null → 404），不洩漏存在與否。
/// </summary>
public sealed class ProjectService(IVibeMakerDbContext db, ICurrentUser currentUser, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<ProjectResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var projects = await db.Projects.AsNoTracking()
            .Where(p => p.UserId == userId && p.Status == ProjectStatus.Active)
            .OrderByDescending(p => p.UpdatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return projects.Select(ToResponse).ToList();
    }

    public async Task<ProjectResponse> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = timeProvider.GetUtcNow();
        var project = Project.Create(currentUser.UserId, request.Name, now);
        db.Projects.Add(project);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditLog.WriteAsync(new AuditEntry(currentUser.ActorName, "project.create", "project", project.Id.ToString("D"), AuditResult.Success, now, null), cancellationToken)
            .ConfigureAwait(false);
        return ToResponse(project);
    }

    /// <returns>專案不存在或不是自己的時回傳 null（→ 404）。</returns>
    public async Task<ProjectResponse?> UpdateAsync(Guid projectId, UpdateProjectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = currentUser.UserId;
        var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        if (request.Name is not null)
        {
            project.Rename(request.Name, now);
        }

        if (request.SystemPrompt is not null)
        {
            project.SetSystemPrompt(request.SystemPrompt, now);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditLog.WriteAsync(new AuditEntry(currentUser.ActorName, "project.update", "project", project.Id.ToString("D"), AuditResult.Success, now, null), cancellationToken)
            .ConfigureAwait(false);
        return ToResponse(project);
    }

    public async Task<ProjectResponse?> GetAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var project = await db.Projects.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        return project is null ? null : ToResponse(project);
    }

    private static ProjectResponse ToResponse(Project p) => new(p.Id, p.Name, p.SystemPrompt, SaValues.Of(p.Status), p.CreatedAt, p.UpdatedAt);
}
