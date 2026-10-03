using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ymir.VibeMaker.Contracts.Conversations;
using Ymir.VibeMaker.Contracts.Workspaces;

namespace Ymir.IntegrationTests.Api;

public class WorkspaceConversationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task CreateAndListWorkspaces_OnlyShowsOwnWorkspaces()
    {
        var ct = TestContext.Current.CancellationToken;
        using var alice = await factory.LoginAsync("ws-alice");
        using var bob = await factory.LoginAsync("ws-bob");

        var created = await alice.PostAsJsonAsync("/api/workspaces", new CreateWorkspaceRequest("  Todo App  "), ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var workspace = (await created.Content.ReadFromJsonAsync<WorkspaceResponse>(JsonDefaults.Options, ct))!;
        Assert.Equal("Todo App", workspace.Name);
        Assert.Equal("ACTIVE", workspace.Status);

        var aliceList = await alice.GetFromJsonAsync<List<WorkspaceResponse>>("/api/workspaces", JsonDefaults.Options, ct);
        var bobList = await bob.GetFromJsonAsync<List<WorkspaceResponse>>("/api/workspaces", JsonDefaults.Options, ct);
        Assert.Contains(aliceList!, w => w.Id == workspace.Id);
        Assert.DoesNotContain(bobList!, w => w.Id == workspace.Id);
    }

    [Fact]
    public async Task CreateWorkspace_WithBlankName_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await factory.LoginAsync("ws-validate");

        var response = await client.PostAsJsonAsync("/api/workspaces", new CreateWorkspaceRequest("   "), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("VALIDATION_FAILED", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ConversationLifecycle_CreateGetListAndEmptyHistory()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await factory.LoginAsync("conv-user");
        var workspace = await client.CreateWorkspaceAsync("Project");

        var created = await client.PostAsJsonAsync("/api/conversations", new CreateConversationRequest(workspace.Id, "第一個對話"), ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var conversation = (await created.Content.ReadFromJsonAsync<ConversationResponse>(JsonDefaults.Options, ct))!;

        var fetched = await client.GetFromJsonAsync<ConversationResponse>($"/api/conversations/{conversation.Id}", JsonDefaults.Options, ct);
        var listed = await client.GetFromJsonAsync<List<ConversationResponse>>($"/api/conversations?workspaceId={workspace.Id}", JsonDefaults.Options, ct);
        var messages = await client.GetFromJsonAsync<List<MessageResponse>>($"/api/conversations/{conversation.Id}/messages", JsonDefaults.Options, ct);

        Assert.Equal("第一個對話", fetched!.Title);
        Assert.Equal(workspace.Id, fetched.WorkspaceId);
        Assert.Single(listed!);
        Assert.Empty(messages!);
    }

    [Fact]
    public async Task Runtime_BeforeFirstExecution_IsNotCreated()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await factory.LoginAsync("runtime-user");
        var workspace = await client.CreateWorkspaceAsync("Project");

        var runtime = await client.GetFromJsonAsync<RuntimeStatusResponse>($"/api/workspaces/{workspace.Id}/runtime", JsonDefaults.Options, ct);

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
