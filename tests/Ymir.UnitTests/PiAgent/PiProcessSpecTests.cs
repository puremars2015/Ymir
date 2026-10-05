using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ymir.VibeMaker.Application.Agents;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Infrastructure.PiAgent;

namespace Ymir.UnitTests.PiAgent;

/// <summary>ADR-0004：Agent 程序只拿到使用者的 virtual key，而且只透過環境變數（不在程序參數）。</summary>
public class PiProcessSpecTests
{
    private static readonly AgentRunRequest Request = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "hello", RuntimePaths.Workspace, "sk-virtual-user-key");

    [Fact]
    public void VirtualKey_IsPassedOnlyAsEnvironmentVariable()
    {
        var options = Options.Create(new PiAgentOptions { DevelopmentApiKey = "sk-should-not-be-used" });
        var harness = new PiAgentHarness(null!, options, NullLogger<PiAgentHarness>.Instance);

        var spec = harness.BuildProcessSpec(Request);

        Assert.Equal("sk-virtual-user-key", spec.Environment!["LITELLM_API_KEY"]);
        Assert.DoesNotContain(spec.Arguments, a => a.Contains("sk-", StringComparison.Ordinal));
        Assert.DoesNotContain(spec.Environment.Values, v => v == "sk-should-not-be-used");
    }

    [Fact]
    public void RunRequest_ToString_DoesNotLeakKeyOrPrompt()
    {
        var text = Request.ToString();
        Assert.DoesNotContain("sk-virtual-user-key", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hello", text, StringComparison.Ordinal);
    }
}
