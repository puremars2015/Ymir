using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Infrastructure.Containers;
using Ymir.VibeMaker.Infrastructure.Runtime;

namespace Ymir.UnitTests.Containers;

/// <summary>驗證 SA §7 / §12 的 container 安全規則；Podman（正式）與 Docker（開發 / 驗證，ADR-0005）都必須符合。</summary>
public class ContainerCommandBuilderTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    public static TheoryData<RuntimeProvider> Engines => new() { RuntimeProvider.Podman, RuntimeProvider.Docker };

    private static IReadOnlyList<string> RunArguments(RuntimeOptions options)
    {
        var directories = WorkspaceDirectories.For(options.WorkspaceRoot, WorkspaceId);
        return ContainerCommandBuilder.BuildRunArguments(options, WorkspaceId, Guid.NewGuid(), directories);
    }

    private static IReadOnlyList<string> RunArguments(RuntimeProvider provider) =>
        RunArguments(new RuntimeOptions { Provider = provider, WorkspaceRoot = "/srv/ymir/workspaces" });

    private static string? ValueAfter(IReadOnlyList<string> args, string flag)
    {
        var index = args.ToList().IndexOf(flag);
        return index >= 0 && index + 1 < args.Count ? args[index + 1] : null;
    }

    private static List<string> ValuesAfter(IReadOnlyList<string> args, string flag) =>
        args.Select((value, index) => (value, index)).Where(x => x.value == flag).Select(x => args[x.index + 1]).ToList();

    [Theory]
    [MemberData(nameof(Engines))]
    public void Run_AppliesHardeningFlags(RuntimeProvider provider)
    {
        var args = RunArguments(provider);

        Assert.Equal("ALL", ValueAfter(args, "--cap-drop"));
        Assert.Equal("no-new-privileges", ValueAfter(args, "--security-opt"));
        Assert.Contains("--read-only", args);
        Assert.Contains("--init", args);
        Assert.Equal("512", ValueAfter(args, "--pids-limit"));
        Assert.Equal("2g", ValueAfter(args, "--memory"));
        Assert.Equal("1.0", ValueAfter(args, "--cpus"));
        Assert.DoesNotContain("--privileged", args);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Run_MountsOnlyThisWorkspaceDirectories_AndNeverTheContainerSocket(RuntimeProvider provider)
    {
        var args = RunArguments(provider);
        var mounts = ValuesAfter(args, "--mount");

        Assert.Equal(
            [
                $"type=bind,source=/srv/ymir/workspaces/{WorkspaceId:N}/workspace,target={RuntimePaths.Workspace}",
                $"type=bind,source=/srv/ymir/workspaces/{WorkspaceId:N}/agent-state,target={RuntimePaths.AgentState}",
            ],
            mounts);
        Assert.DoesNotContain("--volume", args);
        Assert.DoesNotContain(args, a => a.Contains(".sock", StringComparison.Ordinal));
    }

    [Fact]
    public void Podman_UsesKeepIdUserNamespaceAndSlirpNetwork()
    {
        var args = RunArguments(RuntimeProvider.Podman);

        Assert.Equal("keep-id:uid=1000,gid=1000", ValueAfter(args, "--userns"));
        Assert.Equal("slirp4netns", ValueAfter(args, "--network"));
        Assert.DoesNotContain("--user", args);
    }

    [Fact]
    public void Docker_RunsAsAgentUserWithoutPodmanOnlyFlags()
    {
        var args = RunArguments(RuntimeProvider.Docker);

        Assert.Equal("1000:1000", ValueAfter(args, "--user"));
        Assert.Equal("bridge", ValueAfter(args, "--network"));
        Assert.DoesNotContain("--userns", args);
        Assert.DoesNotContain(args, a => a.Contains("keep-id", StringComparison.Ordinal) || a.Contains("relabel", StringComparison.Ordinal));
    }

    [Fact]
    public void SelinuxRelabel_AppliesOnlyToPodman()
    {
        var podman = RunArguments(new RuntimeOptions { Provider = RuntimeProvider.Podman, WorkspaceRoot = "/srv/ymir", SelinuxRelabel = true });
        var docker = RunArguments(new RuntimeOptions { Provider = RuntimeProvider.Docker, WorkspaceRoot = "/srv/ymir", SelinuxRelabel = true });

        Assert.All(ValuesAfter(podman, "--mount"), m => Assert.EndsWith(",relabel=private", m, StringComparison.Ordinal));
        Assert.All(ValuesAfter(docker, "--mount"), m => Assert.DoesNotContain("relabel", m, StringComparison.Ordinal));
    }

    [Fact]
    public void DockerPrepareMounts_ChownsOnlyTheTwoWorkspaceDirectories_WithoutNetwork()
    {
        var options = new RuntimeOptions { Provider = RuntimeProvider.Docker, WorkspaceRoot = "/srv/ymir/workspaces" };
        var args = ContainerCommandBuilder.BuildDockerPrepareMountsArguments(options, WorkspaceDirectories.For(options.WorkspaceRoot, WorkspaceId));

        Assert.Equal("none", ValueAfter(args, "--network"));
        Assert.Contains("--rm", args);
        Assert.Equal(2, ValuesAfter(args, "--mount").Count);
        Assert.Equal(["chown", "1000:1000", "/workspace", "/agent-state"], args.TakeLast(4));
        Assert.DoesNotContain("--privileged", args);
        Assert.DoesNotContain(args, a => a.Contains(".sock", StringComparison.Ordinal));
    }

    [Fact]
    public void ExplicitNetwork_OverridesEngineDefault()
    {
        var args = RunArguments(new RuntimeOptions { Provider = RuntimeProvider.Docker, WorkspaceRoot = "/srv/ymir", Network = "ymir-agents" });
        Assert.Equal("ymir-agents", ValueAfter(args, "--network"));
    }

    [Fact]
    public void Exec_PassesEnvironmentByNameOnly_SoSecretsDoNotAppearInProcessArguments()
    {
        var spec = new RuntimeProcessSpec("pi", ["--mode", "rpc"], new Dictionary<string, string> { ["LITELLM_API_KEY"] = "secret-value" });

        var args = ContainerCommandBuilder.BuildExecArguments(WorkspaceId, spec);

        Assert.Equal(["exec", "--interactive", "--workdir", "/workspace", "--env", "LITELLM_API_KEY", $"ymir-ws-{WorkspaceId:N}", "pi", "--mode", "rpc"], args);
        Assert.DoesNotContain(args, a => a.Contains("secret-value", StringComparison.Ordinal));
    }

    [Fact]
    public void Stop_UsesShortTimeoutFlagSupportedByBothEngines()
    {
        Assert.Equal(["stop", "-t", "10", $"ymir-ws-{WorkspaceId:N}"], ContainerCommandBuilder.BuildStopArguments(WorkspaceId));
    }

    [Fact]
    public void WorkspaceDirectories_AreDerivedOnlyFromWorkspaceId()
    {
        var directories = WorkspaceDirectories.For("/srv/ymir/../ymir/workspaces", WorkspaceId);
        Assert.Equal($"/srv/ymir/workspaces/{WorkspaceId:N}/workspace", directories.Workspace);
    }

    [Theory]
    [InlineData(RuntimeProvider.Podman, "podman")]
    [InlineData(RuntimeProvider.Docker, "docker")]
    public void Executable_DefaultsByEngine(RuntimeProvider provider, string expected)
    {
        Assert.Equal(expected, new RuntimeOptions { Provider = provider }.ResolvedExecutable);
        Assert.Equal("/usr/local/bin/podman", new RuntimeOptions { Provider = provider, ContainerExecutable = "/usr/local/bin/podman" }.ResolvedExecutable);
    }
}
