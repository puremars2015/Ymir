using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ymir.IntegrationTests.Executions;
using Ymir.VibeMaker.Contracts.Conversations;
using Ymir.VibeMaker.Contracts.Models;
using Ymir.VibeMaker.Contracts.Projects;
using Ymir.VibeMaker.Contracts.Settings;

namespace Ymir.IntegrationTests.Api;

/// <summary>選模型、個人 global system prompt、專案 system prompt 的 API。</summary>
public sealed class MultiModelApiFactory : ApiFactory
{
    protected override void Configure(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("VibeMaker:Pi:ModelId", "minimax");
        builder.UseSetting("VibeMaker:Models:0:Id", "minimax");
        builder.UseSetting("VibeMaker:Models:0:DisplayName", "MiniMax M2");
        builder.UseSetting("VibeMaker:Models:1:Id", "gpt-x");
        builder.UseSetting("VibeMaker:Models:1:DisplayName", "GPT X");
        builder.UseSetting("VibeMaker:Models:1:SupportsImages", "true");
    }
}

public class ModelsAndSettingsTests(MultiModelApiFactory factory) : IClassFixture<MultiModelApiFactory>
{
    [Fact]
    public async Task Models_ListsConfiguredModels_WithDefault()
    {
        using var client = await factory.LoginAsync("models-user");

        var models = await client.GetFromJsonAsync<List<ModelResponse>>("/api/models", JsonDefaults.Options, TestContext.Current.CancellationToken);

        Assert.Equal([new ModelResponse("minimax", "MiniMax M2", true, false), new ModelResponse("gpt-x", "GPT X", false, true)], models);
    }

    [Fact]
    public async Task Settings_AreReadAndWrittenOnlyForTheCurrentUser()
    {
        var ct = TestContext.Current.CancellationToken;
        using var alice = await factory.LoginAsync("settings-alice");
        using var bob = await factory.LoginAsync("settings-bob");

        var empty = await alice.GetFromJsonAsync<UserSettingsResponse>("/api/me/settings", JsonDefaults.Options, ct);
        using var put = await alice.PutAsJsonAsync("/api/me/settings", new UpdateUserSettingsRequest("  請一律用繁體中文回答  "), ct);
        var saved = await put.Content.ReadFromJsonAsync<UserSettingsResponse>(JsonDefaults.Options, ct);
        var aliceAfter = await alice.GetFromJsonAsync<UserSettingsResponse>("/api/me/settings", JsonDefaults.Options, ct);
        var bobAfter = await bob.GetFromJsonAsync<UserSettingsResponse>("/api/me/settings", JsonDefaults.Options, ct);

        Assert.Null(empty!.SystemPrompt);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal("請一律用繁體中文回答", saved!.SystemPrompt);
        Assert.Equal("請一律用繁體中文回答", aliceAfter!.SystemPrompt);
        Assert.Null(bobAfter!.SystemPrompt);

        using var clear = await alice.PutAsJsonAsync("/api/me/settings", new UpdateUserSettingsRequest(""), ct);
        Assert.Null((await clear.Content.ReadFromJsonAsync<UserSettingsResponse>(JsonDefaults.Options, ct))!.SystemPrompt);
    }

    [Fact]
    public async Task Settings_TooLongPrompt_Returns400()
    {
        using var client = await factory.LoginAsync("settings-long");
        using var response = await client.PutAsJsonAsync("/api/me/settings", new UpdateUserSettingsRequest(new string('x', 10_001)), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateProject_ChangesOnlyGivenFields()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await factory.LoginAsync("project-update");
        var project = await client.CreateProjectAsync("網站");

        using var promptOnly = await client.PatchAsJsonAsync($"/api/projects/{project.Id}", new UpdateProjectRequest(null, "這個專案使用 Vue"), ct);
        var afterPrompt = await promptOnly.Content.ReadFromJsonAsync<ProjectResponse>(JsonDefaults.Options, ct);
        using var nameOnly = await client.PatchAsJsonAsync($"/api/projects/{project.Id}", new UpdateProjectRequest("行銷網站", null), ct);
        var afterName = await nameOnly.Content.ReadFromJsonAsync<ProjectResponse>(JsonDefaults.Options, ct);
        using var clear = await client.PatchAsJsonAsync($"/api/projects/{project.Id}", new UpdateProjectRequest(null, ""), ct);
        var cleared = await clear.Content.ReadFromJsonAsync<ProjectResponse>(JsonDefaults.Options, ct);

        Assert.Equal(("網站", "這個專案使用 Vue"), (afterPrompt!.Name, afterPrompt.SystemPrompt));
        Assert.Equal(("行銷網站", "這個專案使用 Vue"), (afterName!.Name, afterName.SystemPrompt));
        Assert.Null(cleared!.SystemPrompt);
    }

    [Fact]
    public async Task UpdateProject_InvalidInput_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await factory.LoginAsync("project-update-invalid");
        var project = await client.CreateProjectAsync("p");

        using var blankName = await client.PatchAsJsonAsync($"/api/projects/{project.Id}", new UpdateProjectRequest("  ", null), ct);
        using var longPrompt = await client.PatchAsJsonAsync($"/api/projects/{project.Id}", new UpdateProjectRequest(null, new string('x', 10_001)), ct);

        Assert.Equal(HttpStatusCode.BadRequest, blankName.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, longPrompt.StatusCode);
    }

    [Fact]
    public async Task SendMessage_WithUnknownModel_Returns400_AndValidModelIsRemembered()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await factory.LoginAsync("model-select");
        var conversation = await client.CreateConversationAsync(null, "chat");

        using var bad = await client.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages", new SendMessageRequest("hi", Guid.NewGuid(), "not-a-model"), ct);
        var problem = await bad.Content.ReadFromJsonAsync<JsonElement>(ct);
        var (_, accepted) = await client.SendMessageAsync(conversation.Id, "hi", modelId: "gpt-x");
        await client.ReadEventsAsync(accepted!.EventStreamUrl);
        var after = await client.GetFromJsonAsync<ConversationResponse>($"/api/conversations/{conversation.Id}", JsonDefaults.Options, ct);

        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("MODEL_NOT_AVAILABLE", problem.GetProperty("code").GetString());
        Assert.Equal("gpt-x", after!.ModelId);
    }
}
