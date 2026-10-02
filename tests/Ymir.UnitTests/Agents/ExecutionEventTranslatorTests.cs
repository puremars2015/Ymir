using Ymir.VibeMaker.Application.Agents;
using Ymir.VibeMaker.Contracts.Executions;

namespace Ymir.UnitTests.Agents;

public class ExecutionEventTranslatorTests
{
    private static readonly Guid ExecutionId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static TheoryData<AgentEvent, string, string> Cases => new()
    {
        { new AgentStarted(), ExecutionEventNames.ExecutionStarted, """{"executionId":"11111111-1111-1111-1111-111111111111"}""" },
        { new AgentTextDelta("正在建立"), ExecutionEventNames.AssistantDelta, """{"text":"正在建立"}""" },
        { new AgentToolStarted("bash", "c1", "npm install"), ExecutionEventNames.ToolStarted, """{"tool":"bash","callId":"c1","summary":"npm install"}""" },
        { new AgentToolCompleted("c1", true), ExecutionEventNames.ToolCompleted, """{"callId":"c1","success":true}""" },
        { new AgentStatus("ready"), ExecutionEventNames.Status, """{"text":"ready"}""" },
        { new AgentCompleted("done"), ExecutionEventNames.ExecutionCompleted, """{"executionId":"11111111-1111-1111-1111-111111111111","messageId":null}""" },
        { new AgentFailed("AGENT_TIMEOUT", "timeout"), ExecutionEventNames.ExecutionFailed, """{"code":"AGENT_TIMEOUT","message":"timeout"}""" },
        { new AgentCancelled("partial"), ExecutionEventNames.ExecutionCancelled, """{"executionId":"11111111-1111-1111-1111-111111111111"}""" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Translates_ToSaSseContract(AgentEvent agentEvent, string expectedEventName, string expectedJson)
    {
        var executionEvent = agentEvent.ToExecutionEvent(ExecutionId);

        Assert.Equal(expectedEventName, executionEvent.EventName);
        Assert.Equal(expectedJson, executionEvent.ToJson());
    }
}
