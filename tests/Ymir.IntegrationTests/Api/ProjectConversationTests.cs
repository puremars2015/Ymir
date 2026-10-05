using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ymir.VibeMaker.Contracts.Conversations;
using Ymir.VibeMaker.Contracts.Projects;

namespace Ymir.IntegrationTests.Api;

/// <summary>專案（檔案群組）與對話 API（ADR-0007）。</summary>
public class ProjectConversationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task CreateAndListProjects_OnlyShowsOwnProjects()
    {
        var ct = TestContext.Current.CancellationToken;
        using var alice = await factory.LoginAsync("proj-alice");
        using var bob = await factory.LoginAsync("proj-bob");

        var created = await alice.PostAsJsonAsync("/api/projects", new CreateProjectRequest("  Todo App  "), ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var project = (await created.Content.ReadFromJsonAsync<ProjectResponse>(JsonDefaults.Options, ct))!;
        Assert.Equal("Todo App", project.Name);
        Assert.Equal("ACTIVE", project.Status);

        var aliceList = await alice.GetFromJsonAsync<List<ProjectResponse>>("/api/projects", JsonDefaults.Options, ct);
        var bobList = await bob.GetFromJsonAsync<List<ProjectResponse>>("/api/projects", JsonDefaults.Options, ct);
        Assert.Contains(aliceList!, p => p.Id == project.Id);
        Assert.DoesNotContain(bobList!, p => p.Id == project.Id);
    }

    [Fact]
    public async Task CreateProject_WithBlankName_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await factory.LoginAsync("proj-validate");

        var response = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("   "), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("VALIDATION_FAILED", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ConversationLifecycle_InProject_CreateGetListAndEmptyHistory()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await factory.LoginAsync("conv-user");
        var project = await client.CreateProjectAsync("Project");

        var created = await client.PostAsJsonAsync("/api/conversations", new CreateConversationRequest(project.Id, "第一個對話"), ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var conversation = (await created.Content.ReadFromJsonAsync<ConversationResponse>(JsonDefaults.Options, ct))!;

        var fetched = await client.GetFromJsonAsync<ConversationResponse>($"/api/conversations/{conversation.Id}", JsonDefaults.Options, ct);
        var listed = await client.GetFromJsonAsync<List<ConversationResponse>>($"/api/conversations?projectId={project.Id}", JsonDefaults.Options, ct);
        var messages = await client.GetFromJsonAsync<List<MessageResponse>>($"/api/conversations/{conversation.Id}/messages", JsonDefaults.Options, ct);

        Assert.Equal("第一個對話", fetched!.Title);
        Assert.Equal(project.Id, fetched.ProjectId);
        Assert.Single(listed!);
        Assert.Empty(messages!);
    }

    [Fact]
    public async Task UngroupedConversation_HasNoProject_AndAppearsInFullList()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await factory.LoginAsync("conv-ungrouped");
        var project = await client.CreateProjectAsync("Project");
        var grouped = await client.CreateConversationAsync(project.Id, "專案內");

        var loose = await client.CreateConversationAsync(null, "隨便聊聊");

        Assert.Null(loose.ProjectId);
        var all = await client.GetFromJsonAsync<List<ConversationResponse>>("/api/conversations", JsonDefaults.Options, ct);
        var inProject = await client.GetFromJsonAsync<List<ConversationResponse>>($"/api/conversations?projectId={project.Id}", JsonDefaults.Options, ct);
        Assert.Equal(new HashSet<Guid> { loose.Id, grouped.Id }, all!.Select(c => c.Id).ToHashSet());
        Assert.Equal([grouped.Id], inProject!.Select(c => c.Id));
    }

    [Fact]
    public async Task CreateConversation_InUnknownProject_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await factory.LoginAsync("conv-unknown-project");

        var response = await client.PostAsJsonAsync("/api/conversations", new CreateConversationRequest(Guid.NewGuid(), "x"), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("PROJECT_NOT_FOUND", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Runtime_BeforeFirstExecution_IsNotCreated()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await factory.LoginAsync("runtime-user");

        var runtime = await client.GetFromJsonAsync<RuntimeStatusResponse>("/api/runtime", JsonDefaults.Options, ct);

        Assert.Equal("NOT_CREATED", runtime!.Status);
    }

    [Fact]
    public async Task UnknownConversation_Returns404WithSaCode()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await factory.LoginAsync("conv-404");

        var response = await client.GetAsync(new Uri($"/api/conversations/{Guid.NewGuid()}", UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("CONVERSATION_NOT_FOUND", problem.GetProperty("code").GetString());
    }
}
