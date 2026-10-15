using Ymir.Platform.Messaging;
using Ymir.VibeMaker.Application.Executions;

namespace Ymir.Api.Infrastructure;

/// <summary>
/// 把 Pending 的群組 Bot 訊息更新為執行結果（ADR-0026 §2）。以資料庫為準定期檢查，所以 API 重新啟動後仍會補齊
/// （中斷的 execution 由 <c>ExecutionReconciler</c> 標成失敗，這裡再把 Bot 訊息更新為失敗摘要）。
/// </summary>
internal sealed partial class GroupBotWorker(IServiceScopeFactory scopeFactory, ILogger<GroupBotWorker> logger) : BackgroundService
{
    internal static TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await CompletePendingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // 背景工作不可因單次失敗而停止；下一輪重試。
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    internal async Task CompletePendingAsync(CancellationToken cancellationToken)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var groups = scope.ServiceProvider.GetRequiredService<IChatGroupService>();
            var executions = scope.ServiceProvider.GetRequiredService<ExecutionService>();
            foreach (var pending in await groups.ListPendingBotRepliesAsync(cancellationToken).ConfigureAwait(false))
            {
                if (await executions.GetFinishedReplyAsync(pending.ExecutionId, cancellationToken).ConfigureAwait(false) is { } reply)
                {
                    await groups.CompleteBotAsync(pending.MessageId, reply.Text, reply.Failed, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Completing group bot replies failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
