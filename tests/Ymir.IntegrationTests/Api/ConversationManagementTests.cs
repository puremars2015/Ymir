using System.Net;
using System.Net.Http.Json;
using Ymir.IntegrationTests.Executions;
using Ymir.VibeMaker.Contracts.Conversations;
using Ymir.VibeMaker.Contracts.Projects;

namespace Ymir.IntegrationTests.Api;

/// <summary>對話改名 / 封存（「刪除」）、專案封存、執行中對話的 activeExecutionId（Sprint 3）。</summary>
public class ConversationManagementTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Rename_UpdatesTheTitle()
    {
        using var client = await factory.LoginAsync($"conv-rename-{Guid.NewGuid():N}");
        var conversation = await client.CreateConversationAsync(null, "舊標題");

        using var response = await client.PatchAsJsonAsync($"/api/conversations/{conversation.Id}", new UpdateConversationRequest("新標題"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var renamed = await client.GetFromJsonAsync<ConversationResponse>($"/api/conversations/{conversation.Id}", JsonDefaults.Options, Ct);
        Assert.Equal("新標題", renamed!.Title);
        using var empty = await client.PatchAsJsonAsync($"/api/conversations/{conversation.Id}", new UpdateConversationRequest(" "), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task Archive_HidesTheConversation_AndBlocksFurtherUse()
    {
        using var client = await factory.LoginAsync($"conv-archive-{Guid.NewGuid():N}");
        var conversation = await client.CreateConversationAsync(null, "要刪除的對話");

        using var archived = await client.DeleteAsync($"/api/conversations/{conversation.Id}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, archived.StatusCode);
        var list = await client.GetFromJsonAsync<List<ConversationResponse>>("/api/conversations", JsonDefaults.Options, Ct);
        Assert.DoesNotContain(list!, c => c.Id == conversation.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/conversations/{conversation.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/conversations/{conversation.Id}/messages", Ct)).StatusCode);
        var (send, _) = await client.SendMessageAsync(conversation.Id, "還在嗎");
        Assert.Equal(HttpStatusCode.NotFound, send.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/conversations/{conversation.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task RunningConversation_ReportsItsExecution_AndCannotBeArchived()
    {
        using var client = await factory.LoginAsync($"conv-active-{Guid.NewGuid():N}");
        var conversation = await client.CreateConversationAsync(null, "執行中");

        var (_, sent) = await client.SendMessageAsync(conversation.Id, "hello");
        var during = await client.GetFromJsonAsync<ConversationResponse>($"/api/conversations/{conversation.Id}", JsonDefaults.Options, Ct);
        using var refused = await client.DeleteAsync($"/api/conversations/{conversation.Id}", Ct);
        await client.ReadEventsAsync(sent!.EventStreamUrl);
        var after = await client.GetFromJsonAsync<ConversationResponse>($"/api/conversations/{conversation.Id}", JsonDefaults.Options, Ct);

        Assert.Equal(sent.ExecutionId, during!.ActiveExecutionId);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Null(after!.ActiveExecutionId);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/conversations/{conversation.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task ArchivingAProject_ArchivesItsConversations()
    {
        using var client = await factory.LoginAsync($"proj-archive-{Guid.NewGuid():N}");
        var project = await client.CreateProjectAsync("要刪除的專案");
        var inside = await client.CreateConversationAsync(project.Id, "專案內");
        var outside = await client.CreateConversationAsync(null, "專案外");

        using var archived = await client.DeleteAsync($"/api/projects/{project.Id}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, archived.StatusCode);
        var projects = await client.GetFromJsonAsync<List<ProjectResponse>>("/api/projects", JsonDefaults.Options, Ct);
        Assert.DoesNotContain(projects!, p => p.Id == project.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/projects/{project.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/conversations/{inside.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/conversations/{outside.Id}", Ct)).StatusCode);
        using var create = await client.PostAsJsonAsync("/api/conversations", new CreateConversationRequest(project.Id, "新對話"), Ct);
        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
    }
}
