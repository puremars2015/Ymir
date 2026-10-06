using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ymir.VibeMaker.Application.Executions;
using Ymir.VibeMaker.Application.Runtime;

namespace Ymir.VibeMaker.Infrastructure.Runtime;

/// <summary>
/// 背景維護 runtime（ADR-0011）：啟動時對帳一次，之後每隔 <see cref="ExecutionOptions.IdleCheckInterval"/> 停止閒置的 runtime。
/// 每一輪在自己的 DI scope 執行；任何失敗只記錄 log，下一輪再試。
/// </summary>
internal sealed class RuntimeLifecycleWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ExecutionOptions> options,
    TimeProvider timeProvider,
    ILogger<RuntimeLifecycleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunSafelyAsync((service, ct) => service.ReconcileAsync(ct), "reconcile", stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(options.Value.IdleCheckInterval, timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await RunSafelyAsync((service, ct) => service.StopIdleAsync(ct), "idle stop", stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 關機
        }
    }

    private async Task RunSafelyAsync(Func<RuntimeLifecycleService, CancellationToken, Task<int>> action, string name, CancellationToken stoppingToken)
    {
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                await action(scope.ServiceProvider.GetRequiredService<RuntimeLifecycleService>(), stoppingToken).ConfigureAwait(false);
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
            logger.LogError(ex, "Runtime lifecycle {Task} failed", name);
        }
    }
}
