namespace Ymir.MaintenanceHost;

/// <summary>
/// 定期採樣（預設每 30 秒）；自動回收運作中時，處理持續不足的門檻與依保留天數的回收。
/// 單次失敗只寫 log，下一次採樣繼續；不會因為例外而停止監控。
/// </summary>
internal sealed class MaintenanceWorker(MaintenanceService service, MaintenanceHostOptions options, TimeProvider timeProvider, ILogger<MaintenanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.SampleIntervalSeconds), timeProvider);
        do
        {
            try
            {
                var pressure = await service.SampleAsync(stoppingToken).ConfigureAwait(false);
                foreach (var reading in pressure)
                {
                    await service.RunPressureRoundAsync(reading, stoppingToken).ConfigureAwait(false);
                }

                await service.RunRetentionSweepIfDueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // 背景監控不可因單次失敗而停止；細節只寫 server log。
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogError(ex, "Maintenance sample or reclaim round failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
