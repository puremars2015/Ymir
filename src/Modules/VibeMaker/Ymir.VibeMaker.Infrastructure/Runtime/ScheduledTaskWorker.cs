using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Application.Schedules;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Infrastructure.Runtime;

internal sealed class ScheduledTaskWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ScheduledTaskWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), clock);
        try
        {
            // API 完成 migration 後才讀取排程。
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<ScheduledTaskProcessor>().ReconcileResultsAsync(stoppingToken).ConfigureAwait(false);
                    var now = clock.GetUtcNow();
                    var ids = await scope.ServiceProvider.GetRequiredService<IVibeMakerDbContext>().ScheduledTasks.AsNoTracking()
                        .Where(t => t.Status == ScheduleStatus.Active && t.NextRunAt <= now && (t.LeaseUntil == null || t.LeaseUntil <= now))
                        .OrderBy(t => t.NextRunAt).Take(100).Select(t => t.Id).ToListAsync(stoppingToken).ConfigureAwait(false);
                    foreach (var id in ids)
                    {
                        try
                        {
                            await using var run = scopes.CreateAsyncScope();
                            await run.ServiceProvider.GetRequiredService<ScheduledTaskProcessor>().RunAsync(id, stoppingToken).ConfigureAwait(false);
                        }
#pragma warning disable CA1031 // 個別排程失敗不可停止其他排程；保留租約，稍後以相同冪等鍵重試。
                        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                        { logger.LogError(ex, "Scheduled task {TaskId} failed", id); }
#pragma warning restore CA1031
                    }
                }
#pragma warning disable CA1031 // 資料庫暫時不可用時下一輪重試。
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                { logger.LogError(ex, "Scheduled task poll failed"); }
#pragma warning restore CA1031
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { /* 關機 */ }
    }
}
