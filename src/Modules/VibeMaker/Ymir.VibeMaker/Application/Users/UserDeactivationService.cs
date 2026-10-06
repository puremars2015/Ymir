using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ymir.VibeMaker.Application.Executions;
using Ymir.VibeMaker.Application.Models;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Users;

/// <summary>
/// 帳號停用後清理 Vibe Maker 的資源（SA §12、ADR-0009）：取消執行中的 execution、撤銷 LiteLLM virtual key（ADR-0004）、停止 runtime。
/// 每一步獨立 best effort：任何一步失敗都只記錄 log，不影響停用本身（停用後的請求已經被擋下）。
/// </summary>
public sealed class UserDeactivationService(
    IVibeMakerDbContext db,
    ExecutionService executions,
    RuntimeCredentialService credentials,
    IAgentRuntimeManager runtimes,
    ILogger<UserDeactivationService> logger)
{
    public async Task DeactivateAsync(Guid userId, string actor, CancellationToken cancellationToken)
    {
        try
        {
            var cancelled = await executions.CancelAllForUserAsync(userId, actor, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Cancelled {Count} active executions for disabled user {UserId}", cancelled, userId);
        }
#pragma warning disable CA1031 // best effort：見類別說明。
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to cancel executions for disabled user {UserId}", userId);
        }

        try
        {
            await credentials.RevokeAsync(userId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to revoke model credential for disabled user {UserId}", userId);
        }

        try
        {
            var runtime = await db.AgentRuntimes.AsNoTracking()
                .Where(r => r.UserId == userId && r.Status != RuntimeStatus.Deleted)
                .Select(r => (Guid?)r.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (runtime is { } runtimeId)
            {
                await runtimes.StopAsync(runtimeId, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // runtime manager 重新啟動後不認得舊的 runtime id 時也會到這裡；container 之後由 idle stop 處理（Sprint 4）。
            logger.LogWarning(ex, "Failed to stop runtime for disabled user {UserId}", userId);
        }
    }
}
