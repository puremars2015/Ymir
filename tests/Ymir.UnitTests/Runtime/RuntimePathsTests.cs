using Ymir.VibeMaker.Application.Runtime;

namespace Ymir.UnitTests.Runtime;

/// <summary>ADR-0007：專案用專案目錄（共用檔案），未分組的對話用自己的目錄。</summary>
public class RuntimePathsTests
{
    private static readonly Guid ConversationId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid ProjectId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    [Fact]
    public void WorkingDirectory_UsesProjectDirectory_WhenConversationIsInProject()
    {
        Assert.Equal("/workspace/projects/bbbbbbbb000000000000000000000002", RuntimePaths.WorkingDirectoryFor(ConversationId, ProjectId));
    }

    [Fact]
    public void WorkingDirectory_UsesOwnDirectory_WhenConversationIsUngrouped()
    {
        Assert.Equal("/workspace/chats/aaaaaaaa000000000000000000000001", RuntimePaths.WorkingDirectoryFor(ConversationId, null));
    }

    [Theory]
    [InlineData("/workspace", true)]
    [InlineData("/workspace/projects/bbbbbbbb000000000000000000000002", true)]
    [InlineData("/workspace/chats/aaaaaaaa000000000000000000000001", true)]
    [InlineData("/workspace/", false)]
    [InlineData("/workspace/projects/BBBBBBBB000000000000000000000002", false)]
    [InlineData("/workspace/other/bbbbbbbb000000000000000000000002", false)]
    [InlineData("/workspace/projects/bbbbbbbb000000000000000000000002/..", false)]
    [InlineData("/agent-state", false)]
    public void IsAllowedWorkingDirectory_OnlyAcceptsGeneratedPaths(string path, bool expected)
    {
        Assert.Equal(expected, RuntimePaths.IsAllowedWorkingDirectory(path));
    }
}
