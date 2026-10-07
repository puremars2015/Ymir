using Microsoft.Extensions.Options;
using Ymir.Testing.FakeLlm;
using Ymir.VibeMaker.Application.Agents;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Contracts.Executions;
using Ymir.VibeMaker.Infrastructure.PiAgent;
using Ymir.VibeMaker.Infrastructure.Runtime;

namespace Ymir.IntegrationTests.PiAgent;

/// <summary>Sprint 0 技術驗證（ADR-0003）：Pi RPC 串流、tool call、session 續接、abort、模型錯誤。</summary>
public class PiAgentHarnessTests(PiHarnessFixture fixture) : IClassFixture<PiHarnessFixture>
{
    private static async Task<List<AgentEvent>> RunAsync(IAgentHarness harness, AgentRunRequest request, CancellationToken cancellationToken)
    {
        var events = new List<AgentEvent>();
        await foreach (var agentEvent in harness.RunAsync(request, cancellationToken))
        {
            events.Add(agentEvent);
        }

        return events;
    }

    private void SkipUnlessPiInstalled() =>
        Assert.SkipUnless(fixture.PiAvailable, "pi is not on PATH (npm i -g @earendil-works/pi-coding-agent@1.0.0)");

    [Fact]
    public async Task ToolCall_CreatesFileInWorkingDirectory_AndStreamsEvents()
    {
        SkipUnlessPiInstalled();
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var runtime = await fixture.RuntimeManager.EnsureRuntimeAsync(userId, ct);

        var events = await RunAsync(
            fixture.CreateHarness(),
            PiHarnessFixture.Request(runtime.RuntimeId, Guid.NewGuid(), $"{FakeLlmScript.CreateFileMarker} 請建立檔案", RuntimePaths.ProjectDirectory(projectId)),
            ct);

        Assert.IsType<AgentStarted>(events[0]);
        Assert.Contains(events, e => e is AgentToolStarted { Tool: "bash" });
        Assert.Contains(events, e => e is AgentToolCompleted { Success: true });
        var completed = Assert.IsType<AgentCompleted>(events[^1]);
        Assert.Contains("已完成", completed.FinalText, StringComparison.Ordinal);

        var directories = UserDirectories.For(fixture.WorkspaceRoot, userId);
        var file = Path.Combine(directories.HostPathOf(RuntimePaths.ProjectDirectory(projectId)), FakeLlmScript.CreatedFileName);
        Assert.Equal(FakeLlmScript.CreatedFileContent, await File.ReadAllTextAsync(file, ct));
    }

    [Theory]
    [InlineData(PiHarnessFixture.SecondModelId, true)]
    [InlineData(FakeLlmEndpoints.ModelId, false)]
    public async Task AttachedImages_AreSentToModel_OnlyWhenItSupportsImages(string modelId, bool expectImage)
    {
        SkipUnlessPiInstalled();
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var runtime = await fixture.RuntimeManager.EnsureRuntimeAsync(userId, ct);
        var directory = UserDirectories.For(fixture.WorkspaceRoot, userId).HostPathOf(RuntimePaths.ProjectDirectory(projectId));
        Directory.CreateDirectory(Path.Combine(directory, "uploads"));
        await File.WriteAllBytesAsync(Path.Combine(directory, "uploads", "dot.png"), Ymir.IntegrationTests.Attachments.AttachmentTests.Png, ct);
        await File.WriteAllTextAsync(Path.Combine(directory, "uploads", "notes.txt"), "not an image", ct);

        var events = await RunAsync(
            fixture.CreateHarness(),
            PiHarnessFixture.Request(
                runtime.RuntimeId,
                Guid.NewGuid(),
                "看圖",
                RuntimePaths.ProjectDirectory(projectId),
                modelId,
                attachments: [new AgentAttachment("uploads/dot.png", "image/png", 70), new AgentAttachment("uploads/notes.txt", "text/plain", 12)]),
            ct);

        var completed = Assert.IsType<AgentCompleted>(events[^1]);
        if (expectImage)
        {
            Assert.EndsWith($"{FakeLlmScript.ImageCountPrefix}1", completed.FinalText, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain(FakeLlmScript.ImageCountPrefix, completed.FinalText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task SameSessionId_ResumesConversationHistory()
    {
        SkipUnlessPiInstalled();
        var ct = TestContext.Current.CancellationToken;
        var runtime = await fixture.RuntimeManager.EnsureRuntimeAsync(Guid.NewGuid(), ct);
        var sessionId = Guid.NewGuid();
        var harness = fixture.CreateHarness();

        var first = await RunAsync(harness, PiHarnessFixture.Request(runtime.RuntimeId, sessionId, "第一句", RuntimePaths.Workspace), ct);
        var second = await RunAsync(harness, PiHarnessFixture.Request(runtime.RuntimeId, sessionId, "第二句", RuntimePaths.Workspace), ct);

        Assert.Equal("收到第 1 則使用者訊息：第一句", Assert.IsType<AgentCompleted>(first[^1]).FinalText);
        Assert.Equal("收到第 2 則使用者訊息：第二句", Assert.IsType<AgentCompleted>(second[^1]).FinalText);
    }

    [Fact]
    public async Task Cancellation_AbortsAgent_AndEndsWithCancelled()
    {
        SkipUnlessPiInstalled();
        var runtime = await fixture.RuntimeManager.EnsureRuntimeAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var events = new List<AgentEvent>();
        await foreach (var agentEvent in fixture.CreateHarness().RunAsync(
            PiHarnessFixture.Request(runtime.RuntimeId, Guid.NewGuid(), $"{FakeLlmScript.SlowMarker} 慢慢講", RuntimePaths.Workspace),
            cts.Token))
        {
            events.Add(agentEvent);
            if (events.OfType<AgentTextDelta>().Count() == 3)
            {
                await cts.CancelAsync();
            }
        }

        var cancelled = Assert.IsType<AgentCancelled>(events[^1]);
        Assert.StartsWith("第1段", cancelled.PartialText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ModelProviderError_EndsWithFailed()
    {
        SkipUnlessPiInstalled();
        var ct = TestContext.Current.CancellationToken;
        var runtime = await fixture.RuntimeManager.EnsureRuntimeAsync(Guid.NewGuid(), ct);

        var events = await RunAsync(
            fixture.CreateHarness(autoRetry: false),
            PiHarnessFixture.Request(runtime.RuntimeId, Guid.NewGuid(), $"{FakeLlmScript.FailMarker} 壞掉", RuntimePaths.Workspace),
            ct);

        var failed = Assert.IsType<AgentFailed>(events[^1]);
        Assert.Equal(ExecutionErrorCodes.ModelProviderError, failed.Code);
    }

    [Fact]
    public async Task MissingExecutable_EndsWithAgentRuntimeError()
    {
        var ct = TestContext.Current.CancellationToken;
        var runtime = await fixture.RuntimeManager.EnsureRuntimeAsync(Guid.NewGuid(), ct);
        var harness = new PiAgentHarness(
            fixture.RuntimeManager,
            Options.Create(new PiAgentOptions { Executable = "definitely-not-a-real-pi-binary", ModelBaseUrl = fixture.FakeLlm.BaseUrl }),
            PiHarnessFixture.Catalog,
            new Ymir.VibeMaker.Infrastructure.Files.RuntimeWorkspaceFileReader(fixture.RuntimeManager, new TestOutputLogger<Ymir.VibeMaker.Infrastructure.Files.RuntimeWorkspaceFileReader>()),
            new TestOutputLogger<PiAgentHarness>());

        var events = await RunAsync(harness, PiHarnessFixture.Request(runtime.RuntimeId, Guid.NewGuid(), "hi", RuntimePaths.Workspace), ct);

        var failed = Assert.IsType<AgentFailed>(Assert.Single(events));
        Assert.Equal(ExecutionErrorCodes.AgentRuntimeError, failed.Code);
    }
}
