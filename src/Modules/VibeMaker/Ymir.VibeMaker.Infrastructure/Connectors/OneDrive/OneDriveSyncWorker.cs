using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ymir.VibeMaker.Application.Connectors.OneDrive;
using Ymir.VibeMaker.Application.Executions;

namespace Ymir.VibeMaker.Infrastructure.Connectors.OneDrive;

/// <summary>
/// 處理持久化的 OneDrive 同步工作（ADR-0013 §4）：執行結束後上傳、手動重試、失敗後的自動重試。
/// 工作存在資料庫（<c>onedrive_sync_scopes.upload_pending</c>），服務重啟後繼續。
/// 每個工作取得使用者的執行鎖：使用者正在執行 Agent 時先略過，下一輪（或執行結束排入工作時）再處理，同步期間 Agent 不會同時寫檔。
/// </summary>
internal sealed partial class OneDriveSyncWorker(
    IServiceScopeFactory scopeFactory,
    UserExecutionLocks userLocks,
    OneDriveSyncSignal signal,
    IOptions<OneDriveSyncOptions> options,
    ILogger<OneDriveSyncWorker> logger) : BackgroundService
{
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunDueJobsAsync(stoppingToken).ConfigureAwait(false);
                await signal.WaitAsync(options.Value.PollInterval, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 關機：未完成的工作留在資料庫，下次啟動繼續。
        }
    }

    private async Task RunDueJobsAsync(CancellationToken stoppingToken)
    {
        IReadOnlyList<(Guid ScopeId, Guid UserId)> jobs;
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                jobs = await scope.ServiceProvider.GetRequiredService<OneDriveSyncService>().DueJobsAsync(BatchSize, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // 背景工作失敗（例如資料庫暫時無法使用）不可讓服務停止；下一輪再試。
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogPollFailed(logger, ex);
            return;
        }

        foreach (var (scopeId, userId) in jobs)
        {
            using var userLock = userLocks.TryAcquire(userId);
            if (userLock is null)
            {
                continue; // 使用者正在執行 Agent；執行結束時會再排入工作。
            }

            try
            {
                var scope = scopeFactory.CreateAsyncScope();
                await using (scope.ConfigureAwait(false))
                {
                    await scope.ServiceProvider.GetRequiredService<OneDriveSyncService>().RunJobAsync(scopeId, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // 同上：單一工作的失敗不影響其他工作。
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogJobFailed(logger, ex, scopeId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Polling OneDrive sync jobs failed")]
    private static partial void LogPollFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "OneDrive sync job for scope {ScopeId} crashed")]
    private static partial void LogJobFailed(ILogger logger, Exception exception, Guid scopeId);
}
