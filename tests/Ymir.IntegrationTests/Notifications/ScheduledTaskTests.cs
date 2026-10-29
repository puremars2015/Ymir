using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.Executions;
using Ymir.Platform.Notifications;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Application.Schedules;
using Ymir.VibeMaker.Contracts.Conversations;
using Ymir.VibeMaker.Domain;

namespace Ymir.IntegrationTests.Notifications;

public sealed class ScheduledTaskTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static SaveScheduledTask Request(Guid conversationId, string? cron = null, ScheduleKind kind = ScheduleKind.Reminder, string? model = null) =>
        new(conversationId, kind, "整理報告", model, null, cron, "Asia/Taipei", DateTimeOffset.UtcNow.AddHours(1));
    private static async Task<ScheduledTaskResponse> CreateAsync(HttpClient client, SaveScheduledTask request)
    {
        using var response = await client.PostAsJsonAsync("/api/scheduled-tasks", request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ScheduledTaskResponse>(JsonDefaults.Options, Ct))!;
    }
    [Fact]
    public async Task OwnershipAndConcurrency_AreEnforced()
    {
        using var alice = await factory.LoginAsync($"schedule-a-{Guid.NewGuid():N}");
        using var bob = await factory.LoginAsync($"schedule-b-{Guid.NewGuid():N}");
        var conversation = await alice.CreateConversationAsync(null, "private");
        var request = Request(conversation.Id);
        var task = await CreateAsync(alice, request);
        using var foreignCreate = await bob.PostAsJsonAsync("/api/scheduled-tasks", request, Ct);
        Assert.Equal(HttpStatusCode.NotFound, foreignCreate.StatusCode);
        using var foreignEdit = await bob.PutAsJsonAsync($"/api/scheduled-tasks/{task.Id}", new UpdateScheduledTask(request, task.Version), Ct);
        Assert.Equal(HttpStatusCode.NotFound, foreignEdit.StatusCode);
        foreach (var action in new[] { "pause", "resume", "confirm" })
        {
            using var foreign = await bob.PostAsJsonAsync($"/api/scheduled-tasks/{task.Id}/{action}", new ScheduleVersion(task.Version), Ct);
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }
        using var foreignDelete = await bob.DeleteAsync($"/api/scheduled-tasks/{task.Id}?version={Uri.EscapeDataString(task.Version)}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, foreignDelete.StatusCode);
        using var paused = await alice.PostAsJsonAsync($"/api/scheduled-tasks/{task.Id}/pause", new ScheduleVersion(task.Version), Ct);
        Assert.Equal(HttpStatusCode.OK, paused.StatusCode);
        using var stale = await alice.PutAsJsonAsync($"/api/scheduled-tasks/{task.Id}", new UpdateScheduledTask(request, task.Version), Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Empty((await bob.GetFromJsonAsync<ScheduledTaskResponse[]>("/api/scheduled-tasks", JsonDefaults.Options, Ct))!);
    }
    [Fact]
    public async Task LimitsAndCronValidation_AreEnforced()
    {
        using var client = await factory.LoginAsync($"schedule-limit-{Guid.NewGuid():N}");
        var conversation = await client.CreateConversationAsync(null, "limits");
        using var frequent = await client.PostAsJsonAsync("/api/scheduled-tasks", Request(conversation.Id, "* * * * *"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, frequent.StatusCode);
        for (var i = 0; i < 10; i++) await CreateAsync(client, Request(conversation.Id));
        using var excess = await client.PostAsJsonAsync("/api/scheduled-tasks", Request(conversation.Id), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, excess.StatusCode);
    }
    [Fact]
    public async Task ReminderIsDeliveredOnce_AndArchiveEndsRecurringTasks()
    {
        using var client = await factory.LoginAsync($"schedule-remind-{Guid.NewGuid():N}");
        var conversation = await client.CreateConversationAsync(null, "remind");
        var task = await CreateAsync(client, Request(conversation.Id, "0 * * * *"));
        await MakeDueAsync(task.Id);
        DateTimeOffset? originalDue;
        await using (var scope = factory.Services.CreateAsyncScope())
            originalDue = await scope.ServiceProvider.GetRequiredService<IVibeMakerDbContext>().ScheduledTasks.Where(t => t.Id == task.Id).Select(t => t.NextRunAt).SingleAsync(Ct);
        await RunAsync(task.Id);
        // 模擬提醒已送出、排程狀態尚未推進就崩潰：重放同一個預定時間。
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IVibeMakerDbContext>();
            var saved = await db.ScheduledTasks.SingleAsync(t => t.Id == task.Id, Ct);
            db.Entry(saved).Property(t => t.NextRunAt).CurrentValue = originalDue;
            await db.SaveChangesAsync(Ct);
        }
        await RunAsync(task.Id);
        var notices = (await client.GetFromJsonAsync<NotificationPageResponse>("/api/notifications", JsonDefaults.Options, Ct))!;
        Assert.Equal(NotificationKind.Reminder, Assert.Single(notices.Items).Kind);
        using var archive = await client.DeleteAsync($"/api/conversations/{conversation.Id}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        var tasks = (await client.GetFromJsonAsync<ScheduledTaskResponse[]>("/api/scheduled-tasks", JsonDefaults.Options, Ct))!;
        Assert.Equal(ScheduleStatus.Ended, Assert.Single(tasks).Status);
    }
    [Fact]
    public async Task AgentRun_UsesExistingExecutionFlow_AndMarksMessageSource()
    {
        using var client = await factory.LoginAsync($"schedule-run-{Guid.NewGuid():N}");
        var conversation = await client.CreateConversationAsync(null, "run");
        var models = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/models", Ct);
        var model = models.EnumerateArray().First().GetProperty("id").GetString();
        var task = await CreateAsync(client, Request(conversation.Id, kind: ScheduleKind.AgentRun, model: model));
        await MakeDueAsync(task.Id);
        await RunAsync(task.Id);
        var messages = (await client.GetFromJsonAsync<MessageResponse[]>($"/api/conversations/{conversation.Id}/messages", JsonDefaults.Options, Ct))!;
        var message = Assert.Single(messages, m => m.Role == "USER");
        Assert.Equal("schedule", message.Source);
        Assert.NotNull(message.ExecutionId);
        await client.ReadEventsAsync($"/api/executions/{message.ExecutionId}/events");
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ScheduledTaskProcessor>().ReconcileResultsAsync(Ct);
        var saved = (await client.GetFromJsonAsync<ScheduledTaskResponse[]>("/api/scheduled-tasks", JsonDefaults.Options, Ct))!;
        Assert.Equal("Agent 工作已完成", Assert.Single(saved).LastResult);
    }
    [Fact]
    public async Task RemovedModel_FailsThreeTimesAndPauses()
    {
        using var client = await factory.LoginAsync($"schedule-fail-{Guid.NewGuid():N}");
        var conversation = await client.CreateConversationAsync(null, "fail");
        var models = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/models", Ct);
        var task = await CreateAsync(client, Request(conversation.Id, "0 * * * *", ScheduleKind.AgentRun, models.EnumerateArray().First().GetProperty("id").GetString()));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IVibeMakerDbContext>();
            var saved = await db.ScheduledTasks.SingleAsync(t => t.Id == task.Id, Ct);
            db.Entry(saved).Property(t => t.ModelId).CurrentValue = "removed-model";
            await db.SaveChangesAsync(Ct);
        }
        for (var i = 0; i < 3; i++)
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IVibeMakerDbContext>();
                var saved = await db.ScheduledTasks.SingleAsync(t => t.Id == task.Id, Ct);
                db.Entry(saved).Property(t => t.NextRunAt).CurrentValue = DateTimeOffset.UtcNow.AddMinutes(-i - 1);
                await db.SaveChangesAsync(Ct);
            }
            await RunAsync(task.Id);
        }
        var list = (await client.GetFromJsonAsync<ScheduledTaskResponse[]>("/api/scheduled-tasks", JsonDefaults.Options, Ct))!;
        Assert.Equal(ScheduleStatus.Paused, Assert.Single(list).Status);
        Assert.Equal(3, list[0].ConsecutiveFailures);
        var notices = (await client.GetFromJsonAsync<NotificationPageResponse>("/api/notifications", JsonDefaults.Options, Ct))!;
        Assert.Equal(3, notices.Items.Count);
    }
    [Fact]
    public async Task WritesRequireXsrf_AndAnonymousCannotList()
    {
        using var anonymous = factory.CreateBrowserClient();
        using var listed = await anonymous.GetAsync("/api/scheduled-tasks", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, listed.StatusCode);
        using var client = factory.CreateBrowserClient(sendXsrfHeader: false);
        using var login = await client.PostAsJsonAsync("/api/dev/login", new DevLoginRequest($"schedule-xsrf-{Guid.NewGuid():N}", null, Ymir.Platform.Users.UserRole.User), Ct);
        login.EnsureSuccessStatusCode();
        using var created = await client.PostAsJsonAsync("/api/scheduled-tasks", Request(Guid.NewGuid()), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
    }
    private async Task MakeDueAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IVibeMakerDbContext>();
        var task = await db.ScheduledTasks.SingleAsync(t => t.Id == id, Ct);
        task.Resume(DateTimeOffset.UtcNow.AddMinutes(-1));
        await db.SaveChangesAsync(Ct);
    }
    private async Task RunAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ScheduledTaskProcessor>().RunAsync(id, Ct);
    }
}
