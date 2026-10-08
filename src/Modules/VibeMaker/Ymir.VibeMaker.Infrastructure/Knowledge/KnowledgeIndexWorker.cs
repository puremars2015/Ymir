using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ymir.VibeMaker.Application.Knowledge;

namespace Ymir.VibeMaker.Infrastructure.Knowledge;

/// <summary>
/// 背景建立知識庫索引（ADR-0014 §7）：啟動時把中斷的工作改回等待，之後逐一處理等待中的文件。
/// 工作狀態存在資料庫，服務重啟後繼續；上傳時立即喚醒，輪詢只是保底。
/// </summary>
internal sealed partial class KnowledgeIndexWorker(
    IServiceScopeFactory scopeFactory,
    KnowledgeSignal signal,
    IOptions<KnowledgeOptions> options,
    ILogger<KnowledgeIndexWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.IsEnabled)
        {
            return;
        }

        try
        {
            await RunAsync(
                async service =>
                {
                    await service.ReconcileAsync(stoppingToken).ConfigureAwait(false);
                    return false;
                },
                stoppingToken).ConfigureAwait(false);
            while (!stoppingToken.IsCancellationRequested)
            {
                while (await RunAsync(service => service.IndexNextAsync(stoppingToken), stoppingToken).ConfigureAwait(false))
                {
                }

                await signal.WaitAsync(options.Value.PollInterval, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 關機：未完成的工作下次啟動時對帳。
        }
    }

    private async Task<bool> RunAsync(Func<KnowledgeBaseService, Task<bool>> action, CancellationToken stoppingToken)
    {
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                return await action(scope.ServiceProvider.GetRequiredService<KnowledgeBaseService>()).ConfigureAwait(false);
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
            LogFailed(logger, ex);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Knowledge index worker failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
