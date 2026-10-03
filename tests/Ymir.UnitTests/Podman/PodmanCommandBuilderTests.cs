using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Infrastructure.Podman;
using Ymir.VibeMaker.Infrastructure.Runtime;

namespace Ymir.UnitTests.Podman;

/// <summary>驗證 SA §7 / §12 的 container 安全規則。</summary>
public class PodmanCommandBuilderTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    private static IReadOnlyList<string> RunArguments(RuntimeOptions? options = null)
    {
        options ??= new RuntimeOptions { WorkspaceRoot = "/srv/ymir/workspaces" };
        var directories = WorkspaceDirectories.For(options.WorkspaceRoot, WorkspaceId);
        return PodmanCommandBuilder.BuildRunArguments(options, WorkspaceId, Guid.NewGuid(), directories);
    }

    private static string? ValueAfter(IReadOnlyList<string> args, string flag)
    {
        var index = args.ToList().IndexOf(flag);
        return index >= 0 && index + 1 < args.Count ? args[index + 1] : null;
    }

    [Fact]
    public void Run_AppliesHardeningFlags()
    {
        var args = RunArguments();

        Assert.Equal("ALL", ValueAfter(args, "--cap-drop"));
        Assert.Equal("no-new-privileges", ValueAfter(args, "--security-opt"));
        Assert.Contains("--read-only", args);
        Assert.Contains("--init", args);
        Assert.Equal("keep-id:uid=1000,gid=1000", ValueAfter(args, "--userns"));
        Assert.Equal("512", ValueAfter(args, "--pids-limit"));
        Assert.Equal("2g", ValueAfter(args, "--memory"));
        Assert.Equal("1.0", ValueAfter(args, "--cpus"));
        Assert.DoesNotContain("--privileged", args);
    }

    [Fact]
    public void Run_MountsOnlyThisWorkspaceDirectories_AndNeverTheContainerSocket()
    {
        var args = RunArguments();
        var volumes = args.Select((value, index) => (value, index))
            .Where(x => x.value == "--volume")
            .Select(x => args[x.index + 1])
            .ToList();

        Assert.Equal(2, volumes.Count);
        Assert.Contains($"/srv/ymir/workspaces/{WorkspaceId:N}/workspace:{RuntimePaths.Workspace}:rw", volumes);
        Assert.Contains($"/srv/ymir/workspaces/{WorkspaceId:N}/agent-state:{RuntimePaths.AgentState}:rw", volumes);
        Assert.DoesNotContain(args, a => a.Contains(".sock", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_AddsSelinuxRelabelWhenConfigured()
    {
        var args = RunArguments(new RuntimeOptions { WorkspaceRoot = "/srv/ymir", SelinuxRelabel = true });
        Assert.All(args.Where(a => a.Contains(":/workspace:", StringComparison.Ordinal) || a.Contains(":/agent-state:", StringComparison.Ordinal)),
            v => Assert.EndsWith(":rw,Z", v, StringComparison.Ordinal));
    }

    [Fact]
    public void Exec_PassesEnvironmentByNameOnly_SoSecretsDoNotAppearInProcessArguments()
    {
        var spec = new RuntimeProcessSpec("pi", ["--mode", "rpc"], new Dictionary<string, string> { ["LITELLM_API_KEY"] = "secret-value" });

        var args = PodmanCommandBuilder.BuildExecArguments(WorkspaceId, spec);

        Assert.Equal(["exec", "--interactive", "--workdir", "/workspace", "--env", "LITELLM_API_KEY", $"ymir-ws-{WorkspaceId:N}", "pi", "--mode", "rpc"], args);
        Assert.DoesNotContain(args, a => a.Contains("secret-value", StringComparison.Ordinal));
    }

    [Fact]
    public void WorkspaceDirectories_AreDerivedOnlyFromWorkspaceId()
    {
        var directories = WorkspaceDirectories.For("/srv/ymir/../ymir/workspaces", WorkspaceId);
        Assert.Equal($"/srv/ymir/workspaces/{WorkspaceId:N}/workspace", directories.Workspace);
    }
}
