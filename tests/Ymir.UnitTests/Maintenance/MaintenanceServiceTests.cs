using Microsoft.Extensions.Logging.Abstractions;
using Ymir.Maintenance;
using Ymir.MaintenanceHost;
using Ymir.UnitTests.Models;

namespace Ymir.UnitTests.Maintenance;

/// <summary>ADR-0025 §3、§4：未使用時間、重啟、競態、鎖、門檻與「不強制刪除」。以假的 CLI 與時間驗證，不需要 Podman。</summary>
public sealed class MaintenanceServiceTests : IDisposable
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly string WorkspaceRoot = "/srv/ymir/workspaces";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ymir-mh-" + Guid.NewGuid().ToString("N"));
    private readonly FakeContainerCli _cli = new();
    private readonly FakeHostMetrics _host = new();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

    public MaintenanceServiceTests()
    {
        Directory.CreateDirectory(LockDirectory);
        _host.Directories.Add($"{WorkspaceRoot}/{UserId:N}/workspace");
        _host.Files["/etc/ymir/runtime-host.env"] = "VibeMaker__Runtime__Image=\"localhost/ymir/agent-runtime:current\"\n";
        _host.Filesystems["/home/ymir/storage"] = new FilesystemInfo("/home", 100_000_000_000, 50_000_000_000);
        _cli.Stores["ymir"] = new FakeStore { GraphRoot = "/home/ymir/storage" };
    }

    private string LockDirectory => Path.Combine(_root, "locks");

    private FakeStore Store => _cli.Stores["ymir"];

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private MaintenanceHostOptions Options(bool locks = true) => new()
    {
        Token = new string('t', 40),
        StateDirectory = Path.Combine(_root, "state"),
        LockDirectory = locks ? LockDirectory : Path.Combine(_root, "missing"),
        SustainMinutes = 5,
        CooldownMinutes = 10,
        Stores =
        [
            new MaintenanceStoreOptions
            {
                Name = "ymir",
                Account = "ymir",
                ImageRepositories = ["localhost/ymir/agent-runtime"],
                AgentContainers = true,
                AgentWorkspaceRoot = WorkspaceRoot,
                PinnedImageSources = [new PinnedImageSource { File = "/etc/ymir/runtime-host.env", Key = "VibeMaker__Runtime__Image" }],
            },
        ],
    };

    private MaintenanceService CreateService(MaintenanceHostOptions? options = null)
    {
        options ??= Options();
        var state = new MaintenanceStateStore(options.StateDirectory, options.MaxRunRecords);
        var service = new MaintenanceService(options, _cli, new StoreScanner(_cli, NullLogger<StoreScanner>.Instance), _host, state, _time, NullLogger<MaintenanceService>.Instance);
        service.SetPolicy(ReclamationPolicy.Default with { Enabled = true, ContainerRetentionDays = 7, ImageRetentionDays = 7 });
        return service;
    }

    private FakeContainer AddAgent(string state = "exited")
    {
        Store.Images.Add(new FakeImage { Id = "current" + new string('0', 57), Tags = ["localhost/ymir/agent-runtime:current"], Labels = { ["ymir.managed"] = "true" } });
        var container = new FakeContainer
        {
            Id = "c" + new string('9', 63),
            Name = $"ymir-user-{UserId:N}",
            ImageId = "current" + new string('0', 57),
            State = state,
            Labels = { ["ymir.user-id"] = UserId.ToString("D"), ["ymir.reclaimable"] = "agent-runtime", ["ymir.managed"] = "true" },
            Mounts = { ("bind", $"{WorkspaceRoot}/{UserId:N}/workspace", "/workspace") },
        };
        Store.Containers.Add(container);
        return container;
    }

    private void AddOldImages(int count)
    {
        for (var i = 0; i < count; i++)
        {
            Store.Images.Add(new FakeImage
            {
                Id = OldImageId(i),
                Tags = [$"localhost/ymir/agent-runtime:v{i}"],
                Created = _time.GetUtcNow().AddDays(-100 + i),
                Labels = { ["ymir.managed"] = "true" },
            });
        }
    }

    private static string OldImageId(int i) => $"a{i}" + new string('0', 62);

    [Fact]
    public async Task StoppedAgent_IsDeletedOnlyAfterRetention_WithoutForce()
    {
        var ct = TestContext.Current.CancellationToken;
        AddAgent();
        using var service = CreateService();

        await service.SampleAsync(ct);
        _time.Advance(TimeSpan.FromDays(6));
        Assert.Equal(0, (await service.RunRetentionSweepIfDueAsync(ct))!.Deleted);

        _time.Advance(TimeSpan.FromDays(2));
        var run = await service.RunRetentionSweepIfDueAsync(ct);

        Assert.Equal(1, run!.Deleted);
        Assert.Empty(Store.Containers);
        Assert.DoesNotContain(_cli.Calls, c => c.Arguments.Any(a => a is "-f" or "--force"));
        Assert.Contains(_cli.Calls, c => c.Arguments[0] == "rm" && c.Arguments.Count == 2);
    }

    [Fact]
    public async Task UnusedSince_SurvivesRestart()
    {
        var ct = TestContext.Current.CancellationToken;
        AddAgent();
        var options = Options();
        using (var first = CreateService(options))
        {
            await first.SampleAsync(ct);
        }

        _time.Advance(TimeSpan.FromDays(8));
        using var restarted = CreateService(options);
        var run = await restarted.RunRetentionSweepIfDueAsync(ct);

        Assert.Equal(1, run!.Deleted);
    }

    [Fact]
    public async Task StartingAgainResetsTheUnusedClock()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = AddAgent();
        using var service = CreateService();
        await service.SampleAsync(ct);

        _time.Advance(TimeSpan.FromDays(5));
        agent.State = "running";
        await service.SampleAsync(ct);
        agent.State = "exited";
        await service.SampleAsync(ct);

        _time.Advance(TimeSpan.FromDays(5));
        var run = await service.RunRetentionSweepIfDueAsync(ct);

        Assert.Equal(0, run!.Deleted);
        Assert.Single(Store.Containers);
    }

    [Fact]
    public async Task ContainerStartedJustBeforeDeletion_IsSkipped()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = AddAgent();
        using var service = CreateService();
        await service.SampleAsync(ct);
        _time.Advance(TimeSpan.FromDays(8));
        _cli.BeforeStateCheck = _ => agent.State = "running";

        var run = await service.RunRetentionSweepIfDueAsync(ct);

        Assert.Equal(0, run!.Deleted);
        Assert.Equal(ReclaimItemResult.Skipped, Assert.Single(run.Items).Result);
        Assert.DoesNotContain(_cli.Calls, c => c.Arguments[0] == "rm");
    }

    [Fact]
    public async Task UserLockHeldByRuntimeHost_SkipsDeletion()
    {
        var ct = TestContext.Current.CancellationToken;
        AddAgent();
        using var service = CreateService();
        await service.SampleAsync(ct);
        _time.Advance(TimeSpan.FromDays(8));

        using var held = LifecycleLock.TryAcquire(LockDirectory, LifecycleLock.UserLockFileName(UserId));
        Assert.NotNull(held);
        var run = await service.RunRetentionSweepIfDueAsync(ct);

        Assert.Equal(0, run!.Deleted);
        Assert.Single(Store.Containers);
    }

    [Fact]
    public async Task WithoutLockDirectory_OnlyMonitoringAndDryRun()
    {
        var ct = TestContext.Current.CancellationToken;
        AddAgent();
        using var service = CreateService(Options(locks: false));
        await service.SampleAsync(ct);
        _time.Advance(TimeSpan.FromDays(30));

        Assert.Equal(ReclaimState.LocksUnavailable, service.ReclaimState);
        Assert.Null(await service.RunRetentionSweepIfDueAsync(ct));
        var dryRun = await service.DryRunAsync(ct);
        Assert.Single(dryRun.RetentionCandidates);
        Assert.Single(Store.Containers);
    }

    [Fact]
    public async Task PsFailure_DeletesNothing_AndKeepsObservations()
    {
        var ct = TestContext.Current.CancellationToken;
        AddAgent();
        AddOldImages(5);
        using var service = CreateService();
        await service.SampleAsync(ct);
        _time.Advance(TimeSpan.FromDays(8));
        Store.FailPs = true;

        var run = await service.RunRetentionSweepIfDueAsync(ct);

        Assert.Equal(0, run!.Deleted);
        Assert.DoesNotContain(_cli.Calls, c => c.Arguments[0] is "rm" or "rmi");
        Assert.Equal(DataStatus.Failed, service.GetSnapshot().Stores.Single().Status);

        Store.FailPs = false;
        var after = await service.RunRetentionSweepIfDueAsync(ct);
        Assert.Null(after); // 檢查間隔內不重跑
    }

    [Fact]
    public async Task OldImages_KeepPinnedReferencedAndThreeRecent()
    {
        var ct = TestContext.Current.CancellationToken;
        AddAgent(state: "running");
        AddOldImages(6);
        using var service = CreateService();
        await service.SampleAsync(ct);
        _time.Advance(TimeSpan.FromDays(8));

        var run = await service.RunRetentionSweepIfDueAsync(ct);

        // current（使用中、pinned）+ 最新 3 版保留（current 也算一個最近版本）；刪除最舊的 4 個中不屬於最近 3 版的。
        Assert.Contains(Store.Images, i => i.Tags.Contains("localhost/ymir/agent-runtime:current"));
        Assert.Equal(4, Store.Images.Count);
        Assert.Equal(3, run!.Deleted);
        Assert.DoesNotContain(_cli.Calls, c => c.Arguments.Any(a => a is "-f" or "--force"));
    }

    [Fact]
    public async Task DeployLockHeld_SkipsImageDeletion()
    {
        var ct = TestContext.Current.CancellationToken;
        AddOldImages(6);
        _host.Files["/etc/ymir/runtime-host.env"] = $"VibeMaker__Runtime__Image={OldImageId(5)}\n";
        using var service = CreateService();
        await service.SampleAsync(ct);
        _time.Advance(TimeSpan.FromDays(8));

        using var deploy = LifecycleLock.TryAcquire(LockDirectory, LifecycleLock.DeployLockFileName);
        var run = await service.RunRetentionSweepIfDueAsync(ct);

        Assert.Equal(0, run!.Deleted);
        Assert.Equal(3, run.Candidates);
        Assert.NotEmpty(run.Items);
        Assert.All(run.Items, i => Assert.Equal(ReclaimItemResult.Skipped, i.Result));
        Assert.Equal(6, Store.Images.Count);
    }

    [Fact]
    public async Task CpuPressure_WithoutSafeCandidates_RecordsSummaryAndStopsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        AddAgent(state: "running");
        using var service = CreateService();
        service.SetPolicy(ReclamationPolicy.Default with { Enabled = true, CpuMinFreePercent = 50 });

        _host.Cpu = new CpuTimes(0, 0);
        await service.SampleAsync(ct);
        IReadOnlyList<PressureReading> fired = [];
        for (var minute = 1; minute <= 6; minute++)
        {
            _time.Advance(TimeSpan.FromMinutes(1));
            _host.Cpu = new CpuTimes((ulong)minute * 10, (ulong)minute * 100); // 90% 使用
            fired = await service.SampleAsync(ct);
            if (fired.Count > 0)
            {
                break;
            }
        }

        var reading = Assert.Single(fired);
        var run = await service.RunPressureRoundAsync(reading, ct);

        Assert.Equal(0, run!.Candidates);
        Assert.Contains("不會停止執行中的工作", run.Summary, StringComparison.Ordinal);
        Assert.Equal("running", Assert.Single(Store.Containers).State);
    }

    [Fact]
    public async Task DiskPressure_IgnoresRetention_ButStopsWhenRecovered()
    {
        var ct = TestContext.Current.CancellationToken;
        AddOldImages(6);
        _host.Files["/etc/ymir/runtime-host.env"] = $"VibeMaker__Runtime__Image={OldImageId(5)}\n";
        _host.Filesystems["/home/ymir/storage"] = new FilesystemInfo("/home", 100_000_000_000, 9_000_000_000);
        // 每刪一個 image 可用空間增加 2 GB。
        _cli.OnFreed = (_, size) =>
        {
            var fs = _host.Filesystems["/home/ymir/storage"];
            _host.Filesystems["/home/ymir/storage"] = fs with { AvailableBytes = fs.AvailableBytes + 2_000_000_000 };
        };
        using var service = CreateService();
        service.SetPolicy(ReclamationPolicy.Default with { Enabled = true, DiskMinAvailableGb = 10 });

        await service.SampleAsync(ct);
        _time.Advance(TimeSpan.FromMinutes(6));
        var reading = Assert.Single(await service.SampleAsync(ct));
        var run = await service.RunPressureRoundAsync(reading, ct);

        // 9 GB → 11 GB 已超過回復門檻（10 GB × 1.05）：刪一個就停止，不清光所有候選。
        Assert.Equal(1, run!.Deleted);
        Assert.Equal(ReclaimTrigger.Disk, run.Trigger);
        Assert.Equal(2_000_000_000, run.Filesystems.Single().AvailableAfterBytes - run.Filesystems.Single().AvailableBeforeBytes);
    }

    [Fact]
    public async Task DisabledPolicy_NeverDeletes()
    {
        var ct = TestContext.Current.CancellationToken;
        AddAgent();
        using var service = CreateService();
        service.SetPolicy(ReclamationPolicy.Default with { Enabled = false, ContainerRetentionDays = 1 });
        await service.SampleAsync(ct);
        _time.Advance(TimeSpan.FromDays(30));

        Assert.Null(await service.RunRetentionSweepIfDueAsync(ct));
        Assert.Single(Store.Containers);
    }
}
