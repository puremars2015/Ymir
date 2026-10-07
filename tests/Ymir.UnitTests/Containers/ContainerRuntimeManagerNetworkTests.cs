using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Infrastructure.Containers;
using Ymir.VibeMaker.Infrastructure.Runtime;

namespace Ymir.UnitTests.Containers;

/// <summary>
/// ADR-0012 A.8：network 只能在建立 container 時決定，政策改變時 EnsureRuntime 依 label 重建。
/// 以模擬 container CLI 的 shell script 驗證實際執行的指令（不需要真的 Podman）。
/// </summary>
public sealed class ContainerRuntimeManagerNetworkTests : IDisposable
{
    private const string FakeCli = """
        #!/bin/sh
        # 模擬 podman：container 狀態存在 $STATE（「狀態|network label」），每次呼叫記錄在 $LOG。
        dir=$(dirname "$0"); state="$dir/state"; log="$dir/log"
        echo "$*" >> "$log"
        case "$1" in
          container)
            [ -f "$state" ] || exit 1
            cat "$state"; exit 0 ;;
          run)
            label=internet
            for a in "$@"; do case "$a" in ymir.network=*) label=${a#ymir.network=} ;; esac; done
            echo "running|$label" > "$state"; echo id; exit 0 ;;
          rm) rm -f "$state"; exit 0 ;;
          start) sed -i 's/^[a-z]*|/running|/' "$state"; exit 0 ;;
          stop) sed -i 's/^[a-z]*|/exited|/' "$state"; exit 0 ;;
          *) exit 0 ;;
        esac
        """;

    private static readonly Guid UserId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ymir-fake-cli-" + Guid.NewGuid().ToString("N"));

    public ContainerRuntimeManagerNetworkTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(CliPath, FakeCli.Replace("\r\n", "\n", StringComparison.Ordinal));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(CliPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private string CliPath => Path.Combine(_directory, "podman");

    private string StatePath => Path.Combine(_directory, "state");

    private List<string> Calls => File.Exists(Path.Combine(_directory, "log")) ? [.. File.ReadAllLines(Path.Combine(_directory, "log"))] : [];

    private ContainerRuntimeManager CreateManager(string? restrictedNetwork = "ymir-agents") =>
        new(
            Options.Create(new RuntimeOptions
            {
                Provider = RuntimeProvider.Podman,
                ContainerExecutable = CliPath,
                WorkspaceRoot = Path.Combine(_directory, "workspaces"),
                RestrictedNetwork = restrictedNetwork,
            }),
            NullLogger<ContainerRuntimeManager>.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NewRuntime_IsCreatedWithTheRequestedNetwork()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "uses a shell script as the container CLI");
        var manager = CreateManager();

        var runtime = await manager.EnsureRuntimeAsync(UserId, RuntimeNetworkAccess.Restricted, Ct);

        Assert.Equal(RuntimeTransition.Created, runtime.Transition);
        var run = Assert.Single(Calls, c => c.StartsWith("run ", StringComparison.Ordinal));
        Assert.Contains("--network ymir-agents", run, StringComparison.Ordinal);
        Assert.Contains("ymir.network=restricted", run, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PolicyChange_RemovesAndRecreatesTheContainer()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "uses a shell script as the container CLI");
        var manager = CreateManager();
        await manager.EnsureRuntimeAsync(UserId, RuntimeNetworkAccess.Internet, Ct);

        var runtime = await manager.EnsureRuntimeAsync(UserId, RuntimeNetworkAccess.Restricted, Ct);

        Assert.Equal(RuntimeTransition.Recreated, runtime.Transition);
        Assert.Contains(Calls, c => c.StartsWith("rm --force", StringComparison.Ordinal));
        Assert.Equal("running|restricted", File.ReadAllText(StatePath).Trim());
    }

    [Fact]
    public async Task LegacyContainerWithoutLabel_IsTreatedAsInternet()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "uses a shell script as the container CLI");
        await File.WriteAllTextAsync(StatePath, "running|\n", Ct);
        var manager = CreateManager();

        var unchanged = await manager.EnsureRuntimeAsync(UserId, RuntimeNetworkAccess.Internet, Ct);
        var recreated = await manager.EnsureRuntimeAsync(UserId, RuntimeNetworkAccess.Restricted, Ct);

        Assert.Equal(RuntimeTransition.None, unchanged.Transition);
        Assert.Equal(RuntimeTransition.Recreated, recreated.Transition);
    }

    [Fact]
    public async Task NoNetworkPreference_KeepsTheExistingContainer()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "uses a shell script as the container CLI");
        var manager = CreateManager();
        await manager.EnsureRuntimeAsync(UserId, RuntimeNetworkAccess.Restricted, Ct);
        await manager.StopForUserAsync(UserId, Ct);

        // 例如讀取檔案：不重建，只啟動；label 不變。
        var runtime = await manager.EnsureRuntimeAsync(UserId, network: null, Ct);

        Assert.Equal(RuntimeTransition.Started, runtime.Transition);
        Assert.DoesNotContain(Calls, c => c.StartsWith("rm ", StringComparison.Ordinal));
        Assert.Equal("running|restricted", File.ReadAllText(StatePath).Trim());
    }

    [Fact]
    public async Task RestrictedWithoutConfiguredNetwork_FailsWithoutTouchingTheContainer()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "uses a shell script as the container CLI");
        await File.WriteAllTextAsync(StatePath, "running|internet\n", Ct);
        var manager = CreateManager(restrictedNetwork: null);

        await Assert.ThrowsAsync<RuntimeNetworkUnavailableException>(() => manager.EnsureRuntimeAsync(UserId, RuntimeNetworkAccess.Restricted, Ct));

        Assert.Empty(Calls);
        Assert.Equal(RestrictedNetworkSupport.NotConfigured, manager.RestrictedNetwork);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 暫存目錄清理失敗不影響結果。
        }
    }
}
