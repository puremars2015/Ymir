using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.Executions;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Application.Agents;
using Ymir.VibeMaker.Application.Extensions;
using Ymir.VibeMaker.Application.Persistence;

namespace Ymir.IntegrationTests.Extensions;

public sealed class PersonalMcpFactory : ApiFactory
{
    public CaptureMcpHarness Capture { get; } = new();
    public FakePersonalProbe Probe { get; } = new();
    protected override void Configure(IWebHostBuilder builder) => builder.ConfigureServices(services =>
    {
        services.RemoveAll<IPersonalMcpProbe>();
        services.AddSingleton<IPersonalMcpProbe>(Probe);
        services.RemoveAll<IAgentHarness>();
        services.AddSingleton<IAgentHarness>(Capture);
    });
}

public sealed class FakePersonalProbe : IPersonalMcpProbe
{
    public string? LastToken { get; private set; }
    public Task<bool> TestAsync(Uri url, string? token, CancellationToken cancellationToken)
    {
        LastToken = token;
        return Task.FromResult(true);
    }
}

public sealed class CaptureMcpHarness : IAgentHarness
{
    public AgentRunRequest? Last { get; private set; }
    public async IAsyncEnumerable<AgentEvent> RunAsync(AgentRunRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Last = request;
        yield return new AgentStarted();
        await Task.CompletedTask;
        yield return new AgentCompleted("fixture");
    }
}

public class PersonalMcpManagementTests(PersonalMcpFactory factory) : IClassFixture<PersonalMcpFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static async Task<Guid> UserIdAsync(HttpClient client) => (await client.GetFromJsonAsync<JsonElement>("/api/me", Ct)).GetProperty("id").GetGuid();

    private async Task GrantAsync(HttpClient client, bool allow)
    {
        using var admin = await factory.LoginAsync("mcp-admin-" + Guid.NewGuid().ToString("N"), UserRole.Admin);
        var effect = allow ? ExtensionGrantSetting.Allow : ExtensionGrantSetting.Deny;
        using var response = await admin.PutAsJsonAsync($"/api/admin/users/{await UserIdAsync(client)}/extensions", new SaveUserExtensionsRequest(ExtensionGrantSetting.Inherit, effect, ExtensionGrantSetting.Allow), JsonDefaults.Options, Ct);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<PersonalMcpInfo> AddAsync(HttpClient client, string name = "my-tools")
    {
        using var response = await client.PostAsJsonAsync("/api/connections/mcp", new AddPersonalMcpRequest(name, "https://tools.example.com/mcp", "private-fixture-token"), Ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("private-fixture-token", json, StringComparison.Ordinal);
        return (await response.Content.ReadFromJsonAsync<PersonalMcpInfo>(JsonDefaults.Options, Ct))!;
    }

    private static async Task RunAsync(HttpClient client)
    {
        var conversation = await client.CreateConversationAsync(null, "MCP test");
        var (_, message) = await client.SendMessageAsync(conversation.Id, "hello");
        var events = await client.ReadEventsAsync(message!.EventStreamUrl);
        Assert.Equal("execution.completed", events[^1].EventType);
    }

    [Fact]
    public async Task Registration_EncryptsToken_RedactsResponses_AndIsolatesOwners()
    {
        using var owner = await factory.LoginAsync("mcp-owner-" + Guid.NewGuid().ToString("N"));
        using var other = await factory.LoginAsync("mcp-other-" + Guid.NewGuid().ToString("N"));
        await GrantAsync(owner, true);
        await GrantAsync(other, true);
        var item = await AddAsync(owner);
        Assert.True(item.HasCredential);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var saved = await scope.ServiceProvider.GetRequiredService<IVibeMakerDbContext>().PersonalMcpServers.AsNoTracking().SingleAsync(s => s.Id == item.Id, Ct);
            Assert.NotEqual("private-fixture-token", saved.ProtectedToken);
            Assert.DoesNotContain("private-fixture-token", saved.ProtectedToken!, StringComparison.Ordinal);
        }
        var mine = await owner.GetStringAsync("/api/connections/mcp", Ct);
        Assert.DoesNotContain("private-fixture-token", mine, StringComparison.Ordinal);
        Assert.Empty((await other.GetFromJsonAsync<PersonalMcpList>("/api/connections/mcp", JsonDefaults.Options, Ct))!.Servers);
        using var toggle = await other.PatchAsJsonAsync($"/api/connections/mcp/{item.Id}", new SetPersonalMcpEnabledRequest(false), Ct);
        using var test = await other.PostAsJsonAsync($"/api/connections/mcp/{item.Id}/test", new { }, Ct);
        using var remove = await other.DeleteAsync($"/api/connections/mcp/{item.Id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, toggle.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, test.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, remove.StatusCode);
        // 不依賴全域能力或測試順序：撤銷另一使用者能力後仍不能得知註冊是否存在。
        await GrantAsync(other, false);
        using var deniedOther = await other.PostAsJsonAsync($"/api/connections/mcp/{item.Id}/test", new { }, Ct);
        using var missing = await other.PostAsJsonAsync($"/api/connections/mcp/{Guid.NewGuid()}/test", new { }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, deniedOther.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var ownTest = await owner.PostAsJsonAsync($"/api/connections/mcp/{item.Id}/test", new { }, Ct);
        ownTest.EnsureSuccessStatusCode();
        Assert.Equal("private-fixture-token", factory.Probe.LastToken);
        Assert.DoesNotContain("private-fixture-token", await ownTest.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnabledRegistration_IsPassedToPersonalExecution_NotToGroupExecution()
    {
        using var owner = await factory.LoginAsync("mcp-run-" + Guid.NewGuid().ToString("N"));
        await GrantAsync(owner, true);
        var item = await AddAsync(owner);
        await RunAsync(owner);
        Assert.Equal(item.Id, Assert.Single(factory.Capture.Last!.PersonalMcp!).Id);
        Assert.DoesNotContain("private-fixture-token", factory.Capture.Last.ToString(), StringComparison.Ordinal);
        using var groupResponse = await owner.PostAsJsonAsync("/api/chat-groups", new CreateChatGroupRequest("private creds test", []), Ct);
        groupResponse.EnsureSuccessStatusCode();
        var group = (await groupResponse.Content.ReadFromJsonAsync<ChatGroupResponse>(JsonDefaults.Options, Ct))!;
        using var sentResponse = await owner.PostAsJsonAsync($"/api/chat-groups/{group.Id}/messages", new SendGroupMessageRequest("@bot hello", Guid.NewGuid()), Ct);
        sentResponse.EnsureSuccessStatusCode();
        var sent = (await sentResponse.Content.ReadFromJsonAsync<SendGroupMessageResponse>(JsonDefaults.Options, Ct))!;
        await owner.ReadEventsAsync($"/api/chat-groups/{group.Id}/executions/{sent.Bot!.ExecutionId}/events");
        Assert.Empty(factory.Capture.Last!.PersonalMcp!);
    }

    [Fact]
    public async Task Revocation_StopsExecutionAndTesting_ButAllowsDisableAndRemoval()
    {
        using var owner = await factory.LoginAsync("mcp-revoke-" + Guid.NewGuid().ToString("N"));
        using (var denied = await owner.PostAsJsonAsync("/api/connections/mcp", new AddPersonalMcpRequest("test", "https://tools.example.com/mcp"), Ct))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await GrantAsync(owner, true);
        var item = await AddAsync(owner);
        await GrantAsync(owner, false);
        await RunAsync(owner);
        Assert.Empty(factory.Capture.Last!.PersonalMcp!);
        using var test = await owner.PostAsJsonAsync($"/api/connections/mcp/{item.Id}/test", new { }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, test.StatusCode);
        using var disable = await owner.PatchAsJsonAsync($"/api/connections/mcp/{item.Id}", new SetPersonalMcpEnabledRequest(false), Ct);
        disable.EnsureSuccessStatusCode();
        using var enable = await owner.PatchAsJsonAsync($"/api/connections/mcp/{item.Id}", new SetPersonalMcpEnabledRequest(true), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, enable.StatusCode);
        using var remove = await owner.DeleteAsync($"/api/connections/mcp/{item.Id}", Ct);
        remove.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Validation_DuplicateAndCountLimits_AreEnforced()
    {
        using var owner = await factory.LoginAsync("mcp-limits-" + Guid.NewGuid().ToString("N"));
        await GrantAsync(owner, true);
        using var invalid = await owner.PostAsJsonAsync("/api/connections/mcp", new AddPersonalMcpRequest("invalid", "http://localhost/secret"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var item = await AddAsync(owner);
        using var duplicate = await owner.PostAsJsonAsync("/api/connections/mcp", new AddPersonalMcpRequest(item.Name, item.Url), Ct);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        for (var i = 1; i < 20; i++) await AddAsync(owner, "server-" + i);
        using var exceeded = await owner.PostAsJsonAsync("/api/connections/mcp", new AddPersonalMcpRequest("extra", item.Url), Ct);
        Assert.Equal(HttpStatusCode.Conflict, exceeded.StatusCode);
    }

    [Theory]
    [InlineData("token\n")]
    [InlineData("token\r\nX-Injected: value")]
    [InlineData("token with space")]
    [InlineData("非ASCII憑證")]
    public async Task InvalidToken_IsRejectedWithoutEchoingIt(string token)
    {
        using var owner = await factory.LoginAsync("mcp-token-" + Guid.NewGuid().ToString("N"));
        await GrantAsync(owner, true);
        using var response = await owner.PostAsJsonAsync("/api/connections/mcp", new AddPersonalMcpRequest("test", "https://tools.example.com/mcp", token), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(token, await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }
}
