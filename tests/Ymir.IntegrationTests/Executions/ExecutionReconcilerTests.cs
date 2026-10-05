using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ymir.IntegrationTests.Api;
using Ymir.VibeMaker.Application.Executions;
using Ymir.VibeMaker.Contracts.Executions;
using Ymir.VibeMaker.Domain;
using Ymir.VibeMaker.Infrastructure.Persistence;

namespace Ymir.IntegrationTests.Executions;

/// <summary>SA §14：服務重啟後 RUNNING 不可永久卡住。</summary>
public class ExecutionReconcilerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task InterruptedRunningExecution_IsMarkedFailedWithTerminalEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        _ = factory.Server;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VibeMakerDbContext>();
        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation.Create(Guid.NewGuid(), null, "chat", now);
        var message = Message.CreateUser(conversation.Id, "hi", 1, now);
        var execution = AgentExecution.Queue(conversation, message, Guid.NewGuid(), now);
        execution.Start(Guid.NewGuid(), Guid.NewGuid(), now); // 模擬上次關機時仍在執行
        db.AddRange(conversation, message, execution);
        await db.SaveChangesAsync(ct);

        await scope.ServiceProvider.GetRequiredService<ExecutionReconciler>().ReconcileAsync(ct);

        var reloaded = await db.AgentExecutions.AsNoTracking().SingleAsync(e => e.Id == execution.Id, ct);
        Assert.Equal(ExecutionStatus.Failed, reloaded.Status);
        Assert.Equal(ExecutionErrorCodes.AgentRuntimeError, reloaded.ErrorCode);
        var terminal = await db.ExecutionEvents.AsNoTracking().Where(e => e.ExecutionId == execution.Id).OrderByDescending(e => e.Sequence).FirstAsync(ct);
        Assert.Equal(ExecutionEventNames.ExecutionFailed, terminal.EventType);
    }
}
