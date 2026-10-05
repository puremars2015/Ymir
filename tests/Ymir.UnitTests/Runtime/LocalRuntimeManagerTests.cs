using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Infrastructure.Runtime;

namespace Ymir.UnitTests.Runtime;

public class LocalRuntimeManagerTests
{
    private static readonly Guid UserId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    // 根目錄本身含有 "/workspace" 字樣，確保不會被二次改寫。
    private static readonly UserDirectories Directories = UserDirectories.For("/srv/workspaces", UserId);

    private const string UserRoot = "/srv/workspaces/users/6f9619ff8b86d011b42d00c04fc964ff";

    [Theory]
    [InlineData("/workspace", UserRoot + "/workspace")]
    [InlineData("/workspace/src/app.ts", UserRoot + "/workspace/src/app.ts")]
    [InlineData("/agent-state/sessions", UserRoot + "/agent-state/sessions")]
    [InlineData("--mode", "--mode")]
    [InlineData("/workspaces-other", "/workspaces-other")]
    [InlineData("echo /workspace", "echo /workspace")]
    public void MapRuntimePath_RewritesOnlyRuntimePathPrefixes(string value, string expected)
    {
        Assert.Equal(expected, LocalRuntimeManager.MapRuntimePath(value, Directories));
    }

    [Fact]
    public void UserDirectories_AreDerivedOnlyFromUserId()
    {
        var directories = UserDirectories.For("/srv/ymir/../ymir/workspaces", UserId);
        Assert.Equal("/srv/ymir/workspaces/users/6f9619ff8b86d011b42d00c04fc964ff/workspace", directories.Workspace);
        Assert.Equal("/srv/ymir/workspaces/users/6f9619ff8b86d011b42d00c04fc964ff/agent-state", directories.AgentState);
    }

    [Fact]
    public void HostPathOf_MapsProjectAndConversationDirectories()
    {
        var projectId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        Assert.Equal(UserRoot + "/workspace", Directories.HostPathOf(RuntimePaths.Workspace));
        Assert.Equal(UserRoot + "/workspace/projects/11111111222233334444555555555555", Directories.HostPathOf(RuntimePaths.ProjectDirectory(projectId)));
        Assert.Equal(UserRoot + "/workspace/chats/11111111222233334444555555555555", Directories.HostPathOf(RuntimePaths.ConversationDirectory(projectId)));
    }

    [Theory]
    [InlineData("/workspace/../etc")]
    [InlineData("/workspace/projects/../../etc")]
    [InlineData("/workspace/projects/abc")]
    [InlineData("/etc")]
    [InlineData("/agent-state")]
    [InlineData("/workspace/projects/11111111222233334444555555555555/sub")]
    [InlineData("relative")]
    public void HostPathOf_RejectsAnythingButGeneratedDirectories(string path)
    {
        Assert.Throws<ArgumentException>(() => Directories.HostPathOf(path));
    }
}
