using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Infrastructure.Containers;
using Ymir.VibeMaker.Infrastructure.Runtime;

namespace Ymir.UnitTests.Containers;

/// <summary>驗證 SA §7 / §12 的 container 安全規則；Podman（正式）與 Docker（開發 / 驗證，ADR-0005）都必須符合。一個使用者一個 container（ADR-0007）。</summary>
public class ContainerCommandBuilderTests
{
    private static readonly Guid UserId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    private const string UserRoot = "/srv/ymir/workspaces/users/6f9619ff8b86d011b42d00c04fc964ff";

    public static TheoryData<RuntimeProvider> Engines => new() { RuntimeProvider.Podman, RuntimeProvider.Docker };

    private static IReadOnlyList<string> RunArguments(RuntimeOptions options)
    {
        var directories = UserDirectories.For(options.WorkspaceRoot, UserId);
        return ContainerCommandBuilder.BuildRunArguments(options, UserId, Guid.NewGuid(), directories);
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
    public void Run_MountsOnlyThisUsersDirectories_AndNeverTheContainerSocket(RuntimeProvider provider)
    {
        var args = RunArguments(provider);
        var mounts = ValuesAfter(args, "--mount");

        Assert.Equal(
            [
                $"type=bind,source={Path.GetFullPath(UserRoot + "/workspace")},target={RuntimePaths.Workspace}",
                $"type=bind,source={Path.GetFullPath(UserRoot + "/agent-state")},target={RuntimePaths.AgentState}",
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
    public void DockerPrepareMounts_ChownsOnlyTheUsersTwoDirectories_WithoutNetwork()
    {
        var options = new RuntimeOptions { Provider = RuntimeProvider.Docker, WorkspaceRoot = "/srv/ymir/workspaces" };
        var args = ContainerCommandBuilder.BuildDockerPrepareMountsArguments(options, UserDirectories.For(options.WorkspaceRoot, UserId));

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

        var args = ContainerCommandBuilder.BuildExecArguments(UserId, spec);

        Assert.Equal(["exec", "--interactive", "--workdir", "/workspace", "--env", "LITELLM_API_KEY", $"ymir-user-{UserId:N}", "pi", "--mode", "rpc"], args);
        Assert.DoesNotContain(args, a => a.Contains("secret-value", StringComparison.Ordinal));
    }

    [Fact]
    public void Stop_UsesShortTimeoutFlagSupportedByBothEngines()
    {
        Assert.Equal(["stop", "-t", "10", $"ymir-user-{UserId:N}"], ContainerCommandBuilder.BuildStopArguments(UserId));
    }

    [Fact]
    public void ContainerName_AndLabel_IdentifyTheUser()
    {
        var args = RunArguments(RuntimeProvider.Podman);
        Assert.Equal($"ymir-user-{UserId:N}", ValueAfter(args, "--name"));
        Assert.Contains($"ymir.user-id={UserId:D}", ValuesAfter(args, "--label"));
    }

    [Fact]
    public void Exec_UsesProjectWorkingDirectory()
    {
        var projectId = Guid.NewGuid();
        var spec = new RuntimeProcessSpec("pi", [], null, RuntimePaths.ProjectDirectory(projectId));

        var args = ContainerCommandBuilder.BuildExecArguments(UserId, spec);

        Assert.Equal($"/workspace/projects/{projectId:N}", ValueAfter(args, "--workdir"));
    }

    [Theory]
    [InlineData("/etc")]
    [InlineData("/workspace/../etc")]
    [InlineData("/agent-state")]
    [InlineData("/workspace/projects/not-a-guid")]
    public void Exec_RejectsWorkingDirectoriesOutsideAllowedPaths(string workingDirectory)
    {
        var spec = new RuntimeProcessSpec("pi", [], null, workingDirectory);
        Assert.Throws<ArgumentException>(() => ContainerCommandBuilder.BuildExecArguments(UserId, spec));
        Assert.Throws<ArgumentException>(() => ContainerCommandBuilder.BuildEnsureDirectoryArguments(UserId, workingDirectory));
    }

    [Fact]
    public void EnsureDirectory_RunsMkdirInsideTheUsersContainer()
    {
        var conversationId = Guid.NewGuid();
        Assert.Equal(
            ["exec", $"ymir-user-{UserId:N}", "mkdir", "-p", $"/workspace/chats/{conversationId:N}"],
            ContainerCommandBuilder.BuildEnsureDirectoryArguments(UserId, RuntimePaths.ConversationDirectory(conversationId)));
    }

    [Theory]
    [InlineData(RuntimeProvider.Podman, "podman")]
    [InlineData(RuntimeProvider.Docker, "docker")]
    public void Executable_DefaultsByEngine(RuntimeProvider provider, string expected)
    {
        Assert.Equal(expected, new RuntimeOptions { Provider = provider }.ResolvedExecutable);
        Assert.Equal("/usr/local/bin/podman", new RuntimeOptions { Provider = provider, ContainerExecutable = "/usr/local/bin/podman" }.ResolvedExecutable);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Run_InternetAccess_UsesTheDeploymentNetwork_AndLabelsIt(RuntimeProvider provider)
    {
        var args = RunArguments(provider);

        Assert.Equal(provider == RuntimeProvider.Docker ? "bridge" : "slirp4netns", ValueAfter(args, "--network"));
        Assert.Contains("ymir.network=internet", ValuesAfter(args, "--label"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Run_RestrictedAccess_UsesTheConfiguredInternalNetwork(RuntimeProvider provider)
    {
        // ADR-0012 A.8：network 名稱只來自部署設定，Agent / API 請求無法指定。
        var options = new RuntimeOptions { Provider = provider, WorkspaceRoot = "/srv/ymir/workspaces", RestrictedNetwork = "ymir-agents" };
        var args = ContainerCommandBuilder.BuildRunArguments(options, UserId, Guid.NewGuid(), UserDirectories.For(options.WorkspaceRoot, UserId), RuntimeNetworkAccess.Restricted);

        Assert.Equal("ymir-agents", ValueAfter(args, "--network"));
        Assert.Single(ValuesAfter(args, "--network"));
        Assert.Contains("ymir.network=restricted", ValuesAfter(args, "--label"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Run_RestrictedAccess_WithoutConfiguredNetwork_NeverFallsBackToInternet(RuntimeProvider provider)
    {
        var options = new RuntimeOptions { Provider = provider, WorkspaceRoot = "/srv/ymir/workspaces" };

        Assert.Throws<RuntimeNetworkUnavailableException>(() =>
            ContainerCommandBuilder.BuildRunArguments(options, UserId, Guid.NewGuid(), UserDirectories.For(options.WorkspaceRoot, UserId), RuntimeNetworkAccess.Restricted));
    }

    [Theory]
    [InlineData("host")]
    [InlineData("bridge")]
    [InlineData("slirp4netns")]
    [InlineData("none")]
    [InlineData("container:other")]
    [InlineData("ns:/proc/1/ns/net")]
    [InlineData("-x")]
    [InlineData("")]
    public void RestrictedNetwork_RejectsSharedOrSpecialNetworks(string name)
    {
        var options = new RuntimeOptions { RestrictedNetwork = name };

        Assert.False(RuntimeOptions.IsValidRestrictedNetworkName(name));
        Assert.Throws<InvalidOperationException>(options.ValidateRestrictedNetwork);
    }

    [Theory]
    [InlineData("running|restricted", "running", "restricted")]
    [InlineData("exited|", "exited", null)]
    [InlineData("running|<no value>", "running", null)]
    [InlineData("created\n", "created", null)]
    public void Inspect_ParsesStateAndNetworkLabel(string output, string state, string? label)
    {
        Assert.Equal((state, label), ContainerRuntimeManager.ParseInspect(output.Replace("\\n", "\n", StringComparison.Ordinal)));
    }

    [Fact]
    public void NetworkLabel_MissingMeansInternet()
    {
        // A1b 之前建立的 container 沒有 label，可以對外連線；政策要求受限時會被重建。
        Assert.Equal(RuntimeNetworkAccess.Internet, ContainerCommandBuilder.NetworkOfLabel(null));
        Assert.Equal(RuntimeNetworkAccess.Restricted, ContainerCommandBuilder.NetworkOfLabel("restricted"));
    }
}
