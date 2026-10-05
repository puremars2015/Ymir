using System.Net.Http.Json;
using Ymir.VibeMaker.Contracts.Conversations;
using Ymir.VibeMaker.Contracts.Projects;

namespace Ymir.IntegrationTests.Api;

internal static class ApiClientExtensions
{
    public static async Task<ProjectResponse> CreateProjectAsync(this HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest(name), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectResponse>(JsonDefaults.Options, TestContext.Current.CancellationToken))!;
    }

    /// <param name="projectId">null 表示未分組的對話（ADR-0007）。</param>
    public static async Task<ConversationResponse> CreateConversationAsync(this HttpClient client, Guid? projectId, string title)
    {
        var response = await client.PostAsJsonAsync("/api/conversations", new CreateConversationRequest(projectId, title), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ConversationResponse>(JsonDefaults.Options, TestContext.Current.CancellationToken))!;
    }
}
