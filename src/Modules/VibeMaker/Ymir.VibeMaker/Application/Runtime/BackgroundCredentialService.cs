using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Application.Executions;
using Ymir.VibeMaker.Application.Models;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Runtime;

/// <summary>
/// 讓 container 內的背景程序以使用者自己的模型 key 呼叫模型（ADR-0034，修訂 ADR-0004）：
/// 以獨立用途（<see cref="RuntimeCredentialPurpose.Background"/>）發 key，從 stdin 寫進 container tmpfs 的
/// <see cref="BackgroundWork.KeyFile"/>；key 不出現在程序參數、不寫 workspace 或 host 磁碟，container 停止即消失。
/// <list type="bullet">
/// <item>只給 Active 使用者、執行中而且不是平台刻意停止的 runtime；模型權限與每月預算與一般執行相同。</item>
/// <item>群組 runtime（ADR-0026，container 是成員共用的）與 Local provider（沒有 container）一律不寫。</item>
/// <item>管理員關閉背景模型存取時刪除 key 檔。</item>
/// </list>
/// 背景 worker 定期對所有 runtime 重寫（處理 key 換發、container 重新啟動後 tmpfs 清空、API 重啟）；
/// 啟動 runtime 的路徑（execution、對帳自動啟動）另外立即寫一次。
/// </summary>
public sealed partial class BackgroundCredentialService(
    IVibeMakerDbContext db,
    IAgentRuntimeManager runtimes,
    IUserDirectory users,
    RuntimeCredentialService credentials,
    ModelAccessService modelAccess,
    RuntimePolicyService policies,
    UserExecutionLocks userLocks,
    ILogger<BackgroundCredentialService> logger)
{
    private const string LocalProvider = "LOCAL";

    private static readonly RuntimeStatus[] RunningStatuses = [RuntimeStatus.Created, RuntimeStatus.Running, RuntimeStatus.Busy];

    /// <summary>背景 worker 每一輪：對所有符合條件的 runtime 寫入（或刪除）key。正在執行的使用者跳過，由 execution 自己處理。</summary>
    /// <returns>寫入成功的 runtime 數。</returns>
    public async Task<int> SyncAllAsync(CancellationToken cancellationToken)
    {
        var userIds = await db.AgentRuntimes.AsNoTracking()
            .Where(r => RunningStatuses.Contains(r.Status) && !r.StopRequested)
            .Select(r => r.UserId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (userIds.Count == 0)
        {
            return 0;
        }

        var active = (await users.FindManyAsync(userIds, cancellationToken).ConfigureAwait(false))
            .Where(u => u.Status == UserStatus.Active)
            .Select(u => u.Id)
            .ToHashSet();
        var written = 0;
        foreach (var userId in userIds.Where(active.Contains))
        {
            using var userLock = userLocks.TryAcquire(userId);
            if (userLock is null)
            {
                continue;
            }

            if (await TrySyncAsync(userId, cancellationToken).ConfigureAwait(false))
            {
                written++;
            }
        }

        return written;
    }

    /// <summary>
    /// 剛啟動 runtime 後立即寫入（呼叫端已持有使用者的執行鎖，並確認 runtime 屬於使用者本人而不是群組）。
    /// best effort：失敗只寫 log，下一輪背景 worker 再試。
    /// </summary>
    public async Task<bool> SyncAfterStartAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await users.FindAsync(userId, cancellationToken).ConfigureAwait(false) is not { Status: UserStatus.Active })
        {
            return false;
        }

        return await TrySyncAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> TrySyncAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            // 只處理執行中的 container：EnsureRuntime 對停止的 container 會把它啟動，不能由背景 key 的同步造成。
            if (await runtimes.GetStatusForUserAsync(userId, cancellationToken).ConfigureAwait(false) != RuntimeStatus.Running)
            {
                return false;
            }

            var runtime = await runtimes.EnsureRuntimeAsync(userId, cancellationToken).ConfigureAwait(false);
            if (runtime.Provider == LocalProvider)
            {
                return false; // 沒有 container：寫進 /tmp 會落在開發機上（ADR-0034 只支援 container）。
            }

            var key = await GetKeyAsync(userId, runtime.RuntimeId, cancellationToken).ConfigureAwait(false);
            var process = await runtimes.StartProcessAsync(
                runtime.RuntimeId,
                new RuntimeProcessSpec("bash", ["-c", BackgroundWork.WriteKeyScript, "ymir-background-key"]),
                cancellationToken).ConfigureAwait(false);
            await using (process.ConfigureAwait(false))
            {
                if (key is not null)
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(key);
                    await process.StandardInput.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                process.CloseStandardInput();
                var exitCode = await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                if (exitCode != 0)
                {
                    LogWriteFailed(logger, userId, exitCode);
                    return false;
                }
            }

            return true;
        }
#pragma warning disable CA1031 // 背景 key 是 best effort：發 key 或 runtime 暫時失敗不影響其他使用者與 execution。
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogSyncFailed(logger, ex, userId);
            return false;
        }
    }

    /// <returns>null 表示不給 key（管理員關閉，或使用者沒有可用的模型）：寫入端會刪除既有的 key 檔。</returns>
    private async Task<string?> GetKeyAsync(Guid userId, Guid runtimeId, CancellationToken cancellationToken)
    {
        var policy = await policies.GetAsync(cancellationToken).ConfigureAwait(false);
        if (!policy.BackgroundModelAccess)
        {
            return null;
        }

        var models = await modelAccess.GetForUserAsync(userId, cancellationToken).ConfigureAwait(false);
        if (models.Models.Count == 0)
        {
            return null;
        }

        var credential = await credentials.GetAsync(
            userId,
            runtimeId,
            cancellationToken,
            policy.MonthlyBudget,
            [.. models.Models.Select(m => m.Id)],
            RuntimeCredentialPurpose.Background).ConfigureAwait(false);
        return credential.ApiKey;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Writing the background model key for user {UserId} exited with {ExitCode}")]
    private static partial void LogWriteFailed(ILogger logger, Guid userId, int exitCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Syncing the background model key for user {UserId} failed")]
    private static partial void LogSyncFailed(ILogger logger, Exception exception, Guid userId);
}
