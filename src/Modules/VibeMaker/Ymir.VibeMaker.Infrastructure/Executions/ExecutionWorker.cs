using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ymir.VibeMaker.Application.Executions;

namespace Ymir.VibeMaker.Infrastructure.Executions;

/// <summary>
/// 背景執行 execution（與 HTTP request 解耦，開發規劃 §5）。啟動時先做 reconciliation（SA §14），
/// 之後每個 execution 在自己的 DI scope 執行；同一使用者由 <see cref="UserExecutionLocks"/> 序列化（ADR-0007）。
/// </summary>
internal sealed class ExecutionWorker(IServiceScopeFactory scopeFactory, IExecutionDispatcher dispatcher, ILogger<ExecutionWorker> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, Task> _running = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ExecutionReconciler>().ReconcileAsync(stoppingToken).ConfigureAwait(false);
        }

        try
        {
            await foreach (var executionId in dispatcher.DequeueAllAsync(stoppingToken).ConfigureAwait(false))
            {
                _running[executionId] = RunOneAsync(executionId, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 關機
        }

        await Task.WhenAll(_running.Values).ConfigureAwait(false);
    }

    private async Task RunOneAsync(Guid executionId, CancellationToken stoppingToken)
    {
        await Task.Yield();
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ExecutionRunner>().RunAsync(executionId, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 關機中斷：下次啟動由 reconciliation 標成 FAILED。
        }
#pragma warning disable CA1031 // 單一 execution 的失敗不可讓 worker 停止。
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Execution {ExecutionId} crashed in worker", executionId);
        }
        finally
        {
            _running.TryRemove(executionId, out _);
        }
    }
}
