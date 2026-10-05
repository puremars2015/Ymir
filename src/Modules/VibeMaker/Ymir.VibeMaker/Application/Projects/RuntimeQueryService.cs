using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Identity;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Contracts.Projects;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Projects;

/// <summary>查詢目前使用者的 runtime 狀態（一個使用者一個，ADR-0007）。只讀自己的紀錄，不接受任何外部 id。</summary>
public sealed class RuntimeQueryService(IVibeMakerDbContext db, ICurrentUser currentUser)
{
    public async Task<RuntimeStatusResponse> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var runtime = await db.AgentRuntimes.AsNoTracking()
            .Where(r => r.UserId == userId && r.Status != RuntimeStatus.Deleted)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return runtime is null
            ? new RuntimeStatusResponse(SaValues.Of(RuntimeStatus.NotCreated), null, null, null)
            : new RuntimeStatusResponse(SaValues.Of(runtime.Status), runtime.Provider, runtime.ImageVersion, runtime.LastActiveAt);
    }
}
