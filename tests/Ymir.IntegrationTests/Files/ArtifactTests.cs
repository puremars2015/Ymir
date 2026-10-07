using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.Executions;
using Ymir.VibeMaker.Application.Agents;
using Ymir.VibeMaker.Application.Files;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Contracts.Files;

namespace Ymir.IntegrationTests.Files;

public class ArtifactTests(ArtifactTests.ArtifactFactory factory) : IClassFixture<ArtifactTests.ArtifactFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public sealed class ArtifactFactory : ApiFactory
    {
        protected override void Configure(IWebHostBuilder builder) => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAgentHarness>();
            services.AddSingleton<IAgentHarness, DeliveryHarness>();
        });
    }

    private sealed class DeliveryHarness(IAgentRuntimeManager runtime) : IAgentHarness
    {
        public async IAsyncEnumerable<AgentEvent> RunAsync(AgentRunRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            var script = """
                mkdir -p .ymir/tools .ymir/tmp "$1" "$1/node_modules/pkg"
                printf '{"dependencies":{"pdf-parse":"test"}}' > package.json
                printf 'lock' > package-lock.json
                printf 'private' > .ymir/tools/package.json
                printf 'temporary' > .ymir/tmp/temp.txt
                if [ "$2" != analysis ]; then
                  printf 'summary' > "$1/summary.txt"
                  printf 'private' > "$1/.env"
                  printf 'dependency' > "$1/node_modules/pkg/index.js"
                  ln -sf /etc/passwd "$1/escape.txt"
                  ln -sf /etc "$1/escape-directory"
                fi
                if [ "$2" = large ]; then truncate -s 209715201 "$1/oversized.bin"; fi
                if [ "$2" = many ]; then for i in $(seq 1 501); do printf 'x' > "$1/file-$i.txt"; done; fi
                if [ "$2" = multi ]; then printf '{"name":"website"}' > "$1/package.json"; fi
                """;
            var process = await runtime.StartProcessAsync(request.RuntimeId, new RuntimeProcessSpec("bash", ["-c", script, "test-delivery", ArtifactService.DirectoryFor(request.ExecutionId), request.Prompt], WorkingDirectory: request.WorkingDirectory), ct);
            await using (process)
            {
                process.CloseStandardInput();
                await process.StandardOutput.CopyToAsync(Stream.Null, ct);
                Assert.Equal(0, await process.WaitForExitAsync(ct));
            }
            Assert.Contains(request.SystemPrompts, p => p.Contains(ArtifactService.DirectoryFor(request.ExecutionId), StringComparison.Ordinal));
            yield return request.Prompt switch
            {
                "fail" => new AgentFailed("TEST_FAILURE", "failed"),
                "cancel" => new AgentCancelled("cancelled"),
                _ => new AgentCompleted("response"),
            };
        }
    }

    [Theory]
    [InlineData("analysis")]
    [InlineData("fail")]
    [InlineData("cancel")]
    [InlineData("large")]
    [InlineData("many")]
    public async Task NonDeliveries_DoNotPublishArtifacts(string prompt)
    {
        using var client = await factory.LoginAsync($"artifact-empty-{Guid.NewGuid():N}");
        var conversation = await client.CreateConversationAsync(null, "analysis");
        var (_, sent) = await client.SendMessageAsync(conversation.Id, prompt);
        var events = await client.ReadEventsAsync(sent!.EventStreamUrl);
        Assert.Equal(prompt switch { "fail" => "execution.failed", "cancel" => "execution.cancelled", _ => "execution.completed" }, events[^1].EventType);
        var groups = await client.GetFromJsonAsync<List<ArtifactGroupResponse>>($"/api/conversations/{conversation.Id}/artifacts", JsonDefaults.Options, Ct);
        Assert.Empty(groups!);
        var files = await client.GetFromJsonAsync<WorkspaceFilesResponse>($"/api/conversations/{conversation.Id}/files", JsonDefaults.Options, Ct);
        Assert.Equal(["package-lock.json", "package.json"], files!.Files.Select(f => f.Path));
        foreach (var path in new[] { ".ymir/tools/package.json", ".ymir/tmp/temp.txt", $"deliverables/{sent.ExecutionId:N}/summary.txt" })
        {
            using var result = await client.GetAsync($"/api/conversations/{conversation.Id}/files/download?path={Uri.EscapeDataString(path)}", Ct);
            Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        }
        using var zipResponse = await client.GetAsync($"/api/conversations/{conversation.Id}/files/archive", Ct);
        using var zip = new ZipArchive(new MemoryStream(await zipResponse.Content.ReadAsByteArrayAsync(Ct)));
        Assert.Equal(["package-lock.json", "package.json"], zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
        using var artifactsZip = await client.GetAsync($"/api/conversations/{conversation.Id}/artifacts/{sent.ExecutionId}/archive", Ct);
        Assert.Equal(HttpStatusCode.NotFound, artifactsZip.StatusCode);
    }

    [Theory]
    [InlineData("single", 1)]
    [InlineData("multi", 2)]
    public async Task RegisteredDeliveries_PersistAndAreSharedOnlyWithinOwnedProject(string prompt, int count)
    {
        using var owner = await factory.LoginAsync($"artifact-owner-{Guid.NewGuid():N}");
        using var other = await factory.LoginAsync($"artifact-other-{Guid.NewGuid():N}");
        var project = await owner.CreateProjectAsync("delivery");
        var conversation = await owner.CreateConversationAsync(project.Id, "delivery");
        var sibling = await owner.CreateConversationAsync(project.Id, "sibling");
        var unrelated = await owner.CreateConversationAsync(null, "unrelated");
        var (_, sent) = await owner.SendMessageAsync(conversation.Id, prompt);
        await owner.ReadEventsAsync(sent!.EventStreamUrl);
        var url = $"/api/conversations/{conversation.Id}/artifacts";
        var groups = await owner.GetFromJsonAsync<List<ArtifactGroupResponse>>(url, JsonDefaults.Options, Ct);
        var group = Assert.Single(groups!);
        Assert.Equal(sent.ExecutionId, group.ExecutionId);
        Assert.NotNull(group.MessageId);
        Assert.Equal(count, group.Files.Count);
        Assert.Contains(group.Files, f => f.Path == "summary.txt");
        Assert.Equal(count, Assert.Single((await owner.GetFromJsonAsync<List<ArtifactGroupResponse>>(url, JsonDefaults.Options, Ct))!).Files.Count);
        Assert.Single((await owner.GetFromJsonAsync<List<ArtifactGroupResponse>>($"/api/conversations/{sibling.Id}/artifacts", JsonDefaults.Options, Ct))!);
        Assert.Empty((await owner.GetFromJsonAsync<List<ArtifactGroupResponse>>($"/api/conversations/{unrelated.Id}/artifacts", JsonDefaults.Options, Ct))!);
        using var download = await owner.GetAsync($"/api/conversations/{sibling.Id}/artifacts/{sent.ExecutionId}/download?path=summary.txt", Ct);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("summary", await download.Content.ReadAsStringAsync(Ct));
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("sandbox", download.Headers.GetValues("Content-Security-Policy").Single());
        using var archive = await owner.GetAsync($"{url}/{sent.ExecutionId}/archive", Ct);
        using var zip = new ZipArchive(new MemoryStream(await archive.Content.ReadAsByteArrayAsync(Ct)));
        Assert.Equal(group.Files.Select(f => f.Path).Order(StringComparer.Ordinal), zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
        foreach (var suffix in new[] { "", $"/{sent.ExecutionId}/download?path=summary.txt", $"/{sent.ExecutionId}/archive" })
        {
            using var forbidden = await other.GetAsync(url + suffix, Ct);
            Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
        }
        foreach (var path in new[] { "../package.json", ".env", "node_modules/pkg/index.js", "escape.txt", "escape-directory/passwd", "missing.txt" })
        {
            using var result = await owner.GetAsync($"{url}/{sent.ExecutionId}/download?path={Uri.EscapeDataString(path)}", Ct);
            Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModifiedOrSymlinkReplacedArtifacts_CannotBeDownloaded(bool symlink)
    {
        using var client = await factory.LoginAsync($"artifact-modified-{Guid.NewGuid():N}");
        var conversation = await client.CreateConversationAsync(null, "immutable");
        var (_, sent) = await client.SendMessageAsync(conversation.Id, "single");
        await client.ReadEventsAsync(sent!.EventStreamUrl);
        var userId = (await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/me", Ct)).GetProperty("id").GetGuid();
        using var scope = factory.Services.CreateScope();
        var runtime = scope.ServiceProvider.GetRequiredService<IAgentRuntimeManager>();
        var info = await runtime.EnsureRuntimeAsync(userId, Ct);
        var script = symlink ? "rm -- \"$1\" && ln -s /etc/passwd \"$1\"" : "printf changed >> \"$1\"";
        var process = await runtime.StartProcessAsync(info.RuntimeId, new RuntimeProcessSpec("bash", ["-c", script, "test-mutate", ArtifactService.DirectoryFor(sent.ExecutionId) + "/summary.txt"], WorkingDirectory: RuntimePaths.ConversationDirectory(conversation.Id)), Ct);
        await using (process)
        {
            process.CloseStandardInput();
            await process.StandardOutput.CopyToAsync(Stream.Null, Ct);
            Assert.Equal(0, await process.WaitForExitAsync(Ct));
        }
        foreach (var suffix in new[] { "/download?path=summary.txt", "/archive" })
        {
            using var response = await client.GetAsync($"/api/conversations/{conversation.Id}/artifacts/{sent.ExecutionId}{suffix}", Ct);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

}
