using System.Net.Http.Json;
using Ymir.VibeMaker.Contracts.Conversations;
using Ymir.VibeMaker.Contracts.Workspaces;

namespace Ymir.IntegrationTests.Api;

internal static class ApiClientExtensions
{
    public static async Task<WorkspaceResponse> CreateWorkspaceAsync(this HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/workspaces", new CreateWorkspaceRequest(name), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkspaceResponse>(JsonDefaults.Options, TestContext.Current.CancellationToken))!;
    }

    public static async Task<ConversationResponse> CreateConversationAsync(this HttpClient client, Guid workspaceId, string title)
    {
        var response = await client.PostAsJsonAsync("/api/conversations", new CreateConversationRequest(workspaceId, title), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ConversationResponse>(JsonDefaults.Options, TestContext.Current.CancellationToken))!;
    }
}
