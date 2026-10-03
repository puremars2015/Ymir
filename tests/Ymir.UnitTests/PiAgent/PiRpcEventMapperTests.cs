using System.Text.Json;
using Ymir.VibeMaker.Application.Agents;
using Ymir.VibeMaker.Contracts.Executions;
using Ymir.VibeMaker.Infrastructure.PiAgent;

namespace Ymir.UnitTests.PiAgent;

/// <summary>Fixture 為 Pi 1.0.0 對 Fake LLM 的真實 RPC 輸出（system prompt 已裁剪）。</summary>
public class PiRpcEventMapperTests
{
    private static List<AgentEvent> MapFixture(string name)
    {
        var mapper = new PiRpcEventMapper();
        var events = new List<AgentEvent>();
        foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "pi-rpc", name)))
        {
            using var document = JsonDocument.Parse(line);
            events.AddRange(mapper.Map(document.RootElement));
        }

        return events;
    }

    [Fact]
    public void ToolCallRun_MapsTextToolAndCompletion()
    {
        var events = MapFixture("tool-call.jsonl");

        Assert.IsType<AgentStarted>(events[0]);
        var tool = Assert.Single(events.OfType<AgentToolStarted>());
        Assert.Equal("bash", tool.Tool);
        Assert.Equal("printf 'Hello from Ymir' > hello.txt", tool.Summary);
        var toolCompleted = Assert.Single(events.OfType<AgentToolCompleted>());
        Assert.Equal(tool.CallId, toolCompleted.CallId);
        Assert.True(toolCompleted.Success);

        var completed = Assert.IsType<AgentCompleted>(events[^1]);
        Assert.Equal("我來建立檔案。\n\n已完成，檔案 hello.txt 已建立。", completed.FinalText);

        // 串流的 delta 串起來必須等於保存的最終文字。
        Assert.Equal(completed.FinalText, string.Concat(events.OfType<AgentTextDelta>().Select(d => d.Text)));
    }

    [Fact]
    public void ResumedSession_MapsPlainTextRun()
    {
        var events = MapFixture("resumed-text.jsonl");

        Assert.IsType<AgentStarted>(events[0]);
        var completed = Assert.IsType<AgentCompleted>(events[^1]);
        Assert.Equal("收到第 2 則使用者訊息：第二句話", completed.FinalText);
        Assert.Empty(events.OfType<AgentToolStarted>());
    }

    [Fact]
    public void AbortedRun_MapsToCancelledWithPartialText()
    {
        var events = MapFixture("aborted.jsonl");

        var cancelled = Assert.IsType<AgentCancelled>(events[^1]);
        Assert.StartsWith("第1段", cancelled.PartialText, StringComparison.Ordinal);
        Assert.DoesNotContain(events, e => e is AgentCompleted or AgentFailed);
    }

    [Fact]
    public void ProviderErrorAfterRetries_MapsToModelProviderErrorWithoutLeakingDetail()
    {
        var mapper = new PiRpcEventMapper();
        var events = new List<AgentEvent>();
        foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "pi-rpc", "provider-error-with-retries.jsonl")))
        {
            using var document = JsonDocument.Parse(line);
            events.AddRange(mapper.Map(document.RootElement));
        }

        Assert.Single(events.OfType<AgentStarted>());
        Assert.Equal(3, events.OfType<AgentStatus>().Count());

        var failed = Assert.IsType<AgentFailed>(events[^1]);
        Assert.Equal(ExecutionErrorCodes.ModelProviderError, failed.Code);
        Assert.DoesNotContain("fake upstream failure", failed.Message, StringComparison.Ordinal);
        Assert.Contains("fake upstream failure", mapper.LastErrorDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void EventsAfterTerminal_AreIgnored()
    {
        var mapper = new PiRpcEventMapper();
        using var settled = JsonDocument.Parse("""{"type":"agent_settled"}""");
        using var delta = JsonDocument.Parse("""{"type":"message_update","assistantMessageEvent":{"type":"text_delta","delta":"late"}}""");

        Assert.IsType<AgentCompleted>(Assert.Single(mapper.Map(settled.RootElement)));
        Assert.Empty(mapper.Map(delta.RootElement));
    }

    [Theory]
    [InlineData("bash", """{"command":"npm install\nnpm test"}""", "npm install")]
    [InlineData("write", """{"path":"src/app.ts","content":"SECRET=abc"}""", "src/app.ts")]
    [InlineData("unknown_tool", """{"anything":"x"}""", "unknown_tool")]
    public void SummarizeToolCall_ShowsOnlyCommandOrPath(string tool, string argsJson, string expected)
    {
        using var args = JsonDocument.Parse(argsJson);
        Assert.Equal(expected, PiRpcEventMapper.SummarizeToolCall(tool, args.RootElement));
    }

    [Fact]
    public void SummarizeToolCall_TruncatesLongCommands()
    {
        using var args = JsonDocument.Parse(JsonSerializer.Serialize(new { command = new string('x', 500) }));
        var summary = PiRpcEventMapper.SummarizeToolCall("bash", args.RootElement);
        Assert.Equal(PiRpcEventMapper.MaxSummaryLength, summary.Length);
        Assert.EndsWith("…", summary, StringComparison.Ordinal);
    }
}
