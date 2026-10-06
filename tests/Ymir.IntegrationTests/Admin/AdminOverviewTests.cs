using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.Executions;
using Ymir.Platform.Auditing;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Contracts.Executions;

namespace Ymir.IntegrationTests.Admin;

/// <summary>Admin 總覽、停止 runtime、稽核紀錄查詢（ADR-0010）。</summary>
public class AdminOverviewTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()!;

    [Fact]
    public async Task Overview_CountsUsersExecutionsAndRuntimes()
    {
        using var worker = await factory.LoginAsync($"overview-user-{Guid.NewGuid():N}");
        var conversation = await worker.CreateConversationAsync(null, "overview");
        var (_, sent) = await worker.SendMessageAsync(conversation.Id, "hello");
        var events = await worker.ReadEventsAsync(sent!.EventStreamUrl);
        Assert.Equal(ExecutionEventNames.ExecutionCompleted, events[^1].EventType);
        var me = await worker.GetFromJsonAsync<JsonElement>("/api/me", Ct);
        var workerId = me.GetProperty("id").GetGuid();

        using var admin = await factory.LoginAsync($"overview-admin-{Guid.NewGuid():N}", UserRole.Admin);
        var overview = await admin.GetFromJsonAsync<AdminOverviewResponse>("/api/admin/overview?utcOffsetMinutes=480", JsonDefaults.Options, Ct);

        Assert.NotNull(overview);
        Assert.True(overview.Users.Total >= 2);
        Assert.True(overview.Users.Admins >= 1);
        Assert.True(overview.Users.RecentlyActive >= 2);
        Assert.Equal(7, overview.Executions.Trend.Count);
        Assert.True(overview.Executions.CompletedToday >= 1);
        Assert.True(overview.Executions.Trend[^1].Total >= 1);
        var runtime = Assert.Single(overview.Runtimes, r => r.UserId == workerId);
        Assert.Equal(me.GetProperty("displayName").GetString(), runtime.DisplayName);
    }

    [Fact]
    public async Task Overview_RejectsAnUnreasonableOffset()
    {
        using var admin = await factory.LoginAsync($"overview-offset-{Guid.NewGuid():N}", UserRole.Admin);

        var response = await admin.GetAsync("/api/admin/overview?utcOffsetMinutes=9999", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StopRuntime_StopsTheUsersRuntime_AndIsAudited()
    {
        using var worker = await factory.LoginAsync($"stop-user-{Guid.NewGuid():N}");
        var conversation = await worker.CreateConversationAsync(null, "stop");
        var (_, sent) = await worker.SendMessageAsync(conversation.Id, "hello");
        await worker.ReadEventsAsync(sent!.EventStreamUrl);
        var workerId = (await worker.GetFromJsonAsync<JsonElement>("/api/me", Ct)).GetProperty("id").GetGuid();
        using var admin = await factory.LoginAsync($"stop-admin-{Guid.NewGuid():N}", UserRole.Admin);

        var response = await admin.PostAsync($"/api/admin/runtimes/{workerId}/stop", null, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var overview = await admin.GetFromJsonAsync<AdminOverviewResponse>("/api/admin/overview", JsonDefaults.Options, Ct);
        Assert.Equal("Stopped", overview!.Runtimes.Single(r => r.UserId == workerId).Status.ToString());
        var audit = await admin.GetFromJsonAsync<AuditLogPageResponse>($"/api/admin/audit?action=admin.runtime&userId={workerId}", JsonDefaults.Options, Ct);
        var entry = Assert.Single(audit!.Items);
        Assert.Equal("admin.runtime.stop", entry.Action);
        Assert.Equal(workerId.ToString("D"), entry.TargetId);
        Assert.NotNull(entry.TargetName);

        // 之後再送訊息，runtime 會重新啟動（停止不刪除檔案）
        var (_, again) = await worker.SendMessageAsync(conversation.Id, "again");
        var events = await worker.ReadEventsAsync(again!.EventStreamUrl);
        Assert.Equal(ExecutionEventNames.ExecutionCompleted, events[^1].EventType);
    }

    [Fact]
    public async Task StopRuntime_WithoutRuntime_Returns404()
    {
        using var admin = await factory.LoginAsync($"stop-none-{Guid.NewGuid():N}", UserRole.Admin);

        var response = await admin.PostAsync($"/api/admin/runtimes/{Guid.NewGuid()}/stop", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("RUNTIME_NOT_FOUND", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Audit_FiltersByUserActionAndResult_AndPagesNewestFirst()
    {
        var account = $"audit-user-{Guid.NewGuid():N}";
        for (var i = 0; i < 3; i++)
        {
            using var login = await factory.LoginAsync(account);
        }

        using var probe = await factory.LoginAsync(account);
        var userId = (await probe.GetFromJsonAsync<JsonElement>("/api/me", Ct)).GetProperty("id").GetGuid();
        using var admin = await factory.LoginAsync($"audit-admin-{Guid.NewGuid():N}", UserRole.Admin);

        var first = await admin.GetFromJsonAsync<AuditLogPageResponse>($"/api/admin/audit?userId={userId}&action=auth.login&result=Success&take=2", JsonDefaults.Options, Ct);
        Assert.Equal(2, first!.Items.Count);
        Assert.All(first.Items, i => Assert.Equal(userId, i.ActorUserId));
        Assert.All(first.Items, i => Assert.Equal(AuditResult.Success, i.Result));
        Assert.All(first.Items, i => Assert.StartsWith("auth.login", i.Action, StringComparison.Ordinal));
        Assert.True(first.Items[0].Id > first.Items[1].Id);
        Assert.NotNull(first.NextBefore);
        Assert.NotEqual($"user:{userId:D}", first.Items[0].ActorName);

        var second = await admin.GetFromJsonAsync<AuditLogPageResponse>($"/api/admin/audit?userId={userId}&action=auth.login&take=2&before={first.NextBefore}", JsonDefaults.Options, Ct);
        Assert.Equal(2, second!.Items.Count);
        Assert.True(second.Items[0].Id < first.Items[1].Id);
        Assert.Null(second.NextBefore);

        var denied = await admin.GetFromJsonAsync<AuditLogPageResponse>($"/api/admin/audit?userId={userId}&result=Denied", JsonDefaults.Options, Ct);
        Assert.Empty(denied!.Items);
    }
}
