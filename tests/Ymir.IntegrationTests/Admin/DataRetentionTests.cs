using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ymir.Api.Infrastructure;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.Executions;
using Ymir.Platform.Infrastructure.Persistence;
using Ymir.VibeMaker.Infrastructure.Persistence;

namespace Ymir.IntegrationTests.Admin;

/// <summary>背景清理不自動執行（等待時間拉長），測試直接呼叫一次。</summary>
public sealed class RetentionApiFactory : ApiFactory
{
    protected override void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting("Ymir:Retention:AuditLogDays", "30");
        builder.UseSetting("Ymir:Retention:ExecutionEventDays", "7");
        builder.UseSetting("Ymir:Retention:InitialDelayMinutes", "600");
    }
}

/// <summary>保存期限：超過期限的稽核與已結束 execution 的事件被刪除；對話訊息與期限內的資料保留。</summary>
public class DataRetentionTests(RetentionApiFactory factory) : IClassFixture<RetentionApiFactory>
{
    [Fact]
    public async Task Purge_RemovesOnlyExpiredAuditAndFinishedExecutionEvents()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await factory.LoginAsync($"retention-{Guid.NewGuid():N}");
        var conversation = await client.CreateConversationAsync(null, "retention");
        var (_, sent) = await client.SendMessageAsync(conversation.Id, "hello");
        await client.ReadEventsAsync(sent!.EventStreamUrl);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            // 把這次 execution 的事件與一部分稽核改成很久以前
            var vibe = scope.ServiceProvider.GetRequiredService<VibeMakerDbContext>();
            await vibe.Database.ExecuteSqlAsync($"UPDATE vibemaker.execution_events SET created_at = DATEADD(day, -10, SYSDATETIMEOFFSET()) WHERE execution_id = {sent.ExecutionId}", ct);
            var platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            await platform.Database.ExecuteSqlAsync($"UPDATE platform.audit_log SET timestamp = DATEADD(day, -40, SYSDATETIMEOFFSET()) WHERE target_id = {sent.ExecutionId.ToString("D")}", ct);
        }

        var (audits, events) = await factory.Services.GetRequiredService<DataRetentionWorker>().PurgeOnceAsync(ct);

        Assert.True(audits >= 1);
        Assert.True(events >= 1);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var vibe = scope.ServiceProvider.GetRequiredService<VibeMakerDbContext>();
            Assert.False(await vibe.ExecutionEvents.AnyAsync(e => e.ExecutionId == sent.ExecutionId, ct));
            Assert.True(await vibe.Messages.AnyAsync(m => m.ConversationId == conversation.Id, ct)); // 對話內容保留
            var platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            Assert.True(await platform.AuditLog.AnyAsync(a => a.Action == "system.retention.purge", ct));
            Assert.True(await platform.AuditLog.AnyAsync(a => a.Action == "auth.login", ct)); // 期限內的保留
        }

        var messages = await client.GetMessagesAsync(conversation.Id);
        Assert.NotEmpty(messages);
    }
}
