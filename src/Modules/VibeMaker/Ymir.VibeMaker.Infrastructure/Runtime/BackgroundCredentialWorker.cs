using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ymir.VibeMaker.Application.Runtime;

namespace Ymir.VibeMaker.Infrastructure.Runtime;

/// <summary>
/// 定期把背景程序的模型 key 寫進執行中的 container（ADR-0034）：處理 key 換發（有效期 24 小時）、
/// container 重新啟動後 tmpfs 清空、API 重啟，以及管理員關閉背景模型存取時刪除 key。任何失敗只記錄 log。
/// </summary>
internal sealed class BackgroundCredentialWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<BackgroundCredentialWorker> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    /// <summary>啟動後稍等，讓 <see cref="RuntimeLifecycleWorker"/> 先完成對帳與自動啟動。</summary>
    internal static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, timeProvider, stoppingToken).ConfigureAwait(false);
            using var timer = new PeriodicTimer(Interval, timeProvider);
            do
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 關機
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                await scope.ServiceProvider.GetRequiredService<BackgroundCredentialService>().SyncAllAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 關機
        }
#pragma warning disable CA1031 // 背景維護失敗（例如資料庫暫時無法使用）不可讓服務停止。
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Background model key sync failed");
        }
    }
}
