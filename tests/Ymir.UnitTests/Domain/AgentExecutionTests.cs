using Ymir.VibeMaker.Domain;

namespace Ymir.UnitTests.Domain;

public class AgentExecutionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);

    private static (AgentExecution Execution, Message UserMessage) Queue()
    {
        var workspace = Workspace.Create(Guid.NewGuid(), "ws", Now);
        var conversation = Conversation.Create(workspace, "chat", Now);
        var message = Message.CreateUser(conversation.Id, "hello", 1, Now);
        return (AgentExecution.Queue(conversation, message, Guid.NewGuid(), Now), message);
    }

    [Fact]
    public void Queue_LinksUserMessageToExecution()
    {
        var (execution, message) = Queue();
        Assert.Equal(ExecutionStatus.Queued, execution.Status);
        Assert.Equal(execution.Id, message.ExecutionId);
    }

    [Fact]
    public void HappyPath_QueuedRunningCompleted()
    {
        var (execution, _) = Queue();
        var assistantMessageId = Guid.NewGuid();

        execution.Start(Guid.NewGuid(), Guid.NewGuid(), Now);
        execution.Complete(assistantMessageId, Now.AddSeconds(5));

        Assert.Equal(ExecutionStatus.Completed, execution.Status);
        Assert.Equal(assistantMessageId, execution.AssistantMessageId);
        Assert.Equal(Now.AddSeconds(5), execution.EndedAt);
    }

    [Fact]
    public void QueuedExecution_CanBeCancelledOrFailedDirectly()
    {
        var (cancelled, _) = Queue();
        cancelled.Cancel(null, Now);
        Assert.Equal(ExecutionStatus.Cancelled, cancelled.Status);

        var (failed, _) = Queue();
        failed.Fail("AGENT_RUNTIME_ERROR", null, Now);
        Assert.Equal("AGENT_RUNTIME_ERROR", failed.ErrorCode);
    }

    [Fact]
    public void TerminalExecution_RejectsFurtherTransitions()
    {
        var (execution, _) = Queue();
        execution.Cancel(null, Now);

        Assert.Throws<InvalidOperationException>(() => execution.Start(Guid.NewGuid(), Guid.NewGuid(), Now));
        Assert.Throws<InvalidOperationException>(() => execution.Complete(null, Now));
        Assert.Throws<InvalidOperationException>(() => execution.Cancel(null, Now));
    }

    [Fact]
    public void Complete_RequiresRunning()
    {
        var (execution, _) = Queue();
        Assert.Throws<InvalidOperationException>(() => execution.Complete(null, Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Workspace_RequiresName(string name)
    {
        Assert.Throws<DomainValidationException>(() => Workspace.Create(Guid.NewGuid(), name, Now));
    }

    [Fact]
    public void Workspace_TrimsNameAndUsesIdAsStorageKey()
    {
        var workspace = Workspace.Create(Guid.NewGuid(), "  My Project  ", Now);
        Assert.Equal("My Project", workspace.Name);
        Assert.Equal(workspace.Id.ToString("N"), workspace.StorageKey);
    }
}
