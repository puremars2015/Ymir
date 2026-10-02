using Ymir.VibeMaker.Infrastructure.Runtime;

namespace Ymir.UnitTests.Runtime;

public class LocalRuntimeManagerTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    // 根目錄本身含有 "/workspace" 字樣，確保不會被二次改寫。
    private static readonly WorkspaceDirectories Directories = WorkspaceDirectories.For("/srv/workspaces", WorkspaceId);

    [Theory]
    [InlineData("/workspace", "/srv/workspaces/6f9619ff8b86d011b42d00c04fc964ff/workspace")]
    [InlineData("/workspace/src/app.ts", "/srv/workspaces/6f9619ff8b86d011b42d00c04fc964ff/workspace/src/app.ts")]
    [InlineData("/agent-state/sessions", "/srv/workspaces/6f9619ff8b86d011b42d00c04fc964ff/agent-state/sessions")]
    [InlineData("--mode", "--mode")]
    [InlineData("/workspaces-other", "/workspaces-other")]
    [InlineData("echo /workspace", "echo /workspace")]
    public void MapRuntimePath_RewritesOnlyRuntimePathPrefixes(string value, string expected)
    {
        Assert.Equal(expected, LocalRuntimeManager.MapRuntimePath(value, Directories));
    }
}
