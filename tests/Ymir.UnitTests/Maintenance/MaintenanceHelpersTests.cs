using Ymir.Maintenance;
using Ymir.MaintenanceHost;

namespace Ymir.UnitTests.Maintenance;

public sealed class MaintenanceHelpersTests : IDisposable
{
    private readonly string _locks = Path.Combine(Path.GetTempPath(), "ymir-locks-" + Guid.NewGuid().ToString("N"));

    public MaintenanceHelpersTests() => Directory.CreateDirectory(_locks);

    public void Dispose() => Directory.Delete(_locks, recursive: true);

    [Fact]
    public void LifecycleLock_IsExclusiveUntilReleased()
    {
        var name = LifecycleLock.UserLockFileName(Guid.NewGuid());
        var first = LifecycleLock.TryAcquire(_locks, name);
        Assert.NotNull(first);
        Assert.Null(LifecycleLock.TryAcquire(_locks, name));

        first.Dispose();
        using var again = LifecycleLock.TryAcquire(_locks, name);
        Assert.NotNull(again);
    }

    [Fact]
    public void LifecycleLock_RejectsMissingDirectoryAndPathNames()
    {
        Assert.Throws<LifecycleLockUnavailableException>(() => LifecycleLock.TryAcquire(Path.Combine(_locks, "missing"), "deploy.lock"));
        Assert.Throws<ArgumentException>(() => LifecycleLock.TryAcquire(_locks, "../escape.lock"));
    }

    [Fact]
    public void PressureTracker_RequiresSustainAndHonoursCooldownAndHysteresis()
    {
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var tracker = new PressureTracker(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10));
        PressureReading Low(bool recovered = false) => new("disk:fs", ReclaimTrigger.Disk, Below: true, Recovered: recovered);
        PressureReading Ok(bool recovered) => new("disk:fs", ReclaimTrigger.Disk, Below: false, Recovered: recovered);

        Assert.Empty(tracker.Update([Low()], start));
        Assert.Empty(tracker.Update([Low()], start.AddMinutes(4)));
        Assert.Single(tracker.Update([Low()], start.AddMinutes(5)));
        Assert.Empty(tracker.Update([Low()], start.AddMinutes(9))); // 冷卻中
        Assert.Single(tracker.Update([Low()], start.AddMinutes(15)));

        // 回到門檻以上但未達回復門檻：維持觸發狀態，不重設；回復後才解除。
        tracker.Update([Ok(recovered: false)], start.AddMinutes(16));
        Assert.True(tracker.Snapshot().Single().Triggered);
        tracker.Update([Ok(recovered: true)], start.AddMinutes(17));
        Assert.False(tracker.Snapshot().Single().Triggered);

        // 短暫尖峰不觸發。
        Assert.Empty(tracker.Update([Low()], start.AddMinutes(30)));
        Assert.Empty(tracker.Update([Ok(recovered: true)], start.AddMinutes(31)));
        Assert.Empty(tracker.Update([Low()], start.AddMinutes(34)));
    }

    [Fact]
    public void PressureEvaluate_SkipsDisabledThresholds()
    {
        var host = new HostResources(DataStatus.Available, DateTimeOffset.UnixEpoch, 4, 95, 5, 16_000_000_000, 1_000_000_000, 6.25);
        var fs = new FilesystemResources("fs-1", ["root"], 100_000_000_000, 1_000_000_000, 1);

        Assert.Empty(PressureTracker.Evaluate(ReclamationPolicy.Default, 5, host, [fs]));

        var readings = PressureTracker.Evaluate(ReclamationPolicy.Default with { CpuMinFreePercent = 10, MemoryMinAvailablePercent = 10, DiskMinAvailableGb = 5 }, 5, host, [fs]);
        Assert.Equal(["cpu", "memory", "disk:fs-1"], readings.Select(r => r.Key));
        Assert.All(readings, r => Assert.True(r.Below));
    }

    [Theory]
    [InlineData("4.59%", 4.59)]
    [InlineData("0.00%", 0.0)]
    [InlineData("--", null)]
    public void ParsePercent(string text, double? expected) => Assert.Equal(expected, PodmanJson.ParsePercent(text));

    [Theory]
    [InlineData("278.7MB", 278_700_000L)]
    [InlineData("16.23GB ", 16_230_000_000L)]
    [InlineData("512kB", 512_000L)]
    [InlineData("0B", 0L)]
    [InlineData("1.5GiB", 1_610_612_736L)]
    [InlineData("abc", null)]
    public void ParseSize(string text, long? expected) => Assert.Equal(expected, PodmanJson.ParseSize(text));

    [Fact]
    public void ParseImages_MergesDuplicateEntriesAndDropsNoneTags()
    {
        const string json = """
            [
              {"Id":"sha256:abc","Names":["localhost/ymir/api:1"],"Size":10,"Created":100,"Labels":{"ymir.managed":"true"}},
              {"Id":"abc","Names":["localhost/ymir/api:2"],"Size":10,"Created":100,"Labels":null},
              {"Id":"def","Names":null,"RepoTags":["<none>:<none>"],"Size":5,"Created":50}
            ]
            """;
        var images = PodmanJson.ParseImages(json);

        Assert.Equal(["localhost/ymir/api:1", "localhost/ymir/api:2"], images.Single(i => i.Id == "abc").Tags);
        Assert.True(images.Single(i => i.Id == "def").IsDangling);
    }

    [Fact]
    public void HostMetrics_ParsesProcFiles()
    {
        var a = HostMetrics.ParseCpuLine("cpu  100 0 100 700 100 0 0 0 0 0");
        var b = HostMetrics.ParseCpuLine("cpu  200 0 200 1300 100 0 0 0 0 0");
        Assert.Equal(25, HostMetrics.CpuUsedPercent(a, b));
        Assert.Null(HostMetrics.CpuUsedPercent(null, b));

        var memory = HostMetrics.ParseMeminfo("MemTotal:       16000 kB\nMemFree:  100 kB\nMemAvailable:    4000 kB\n");
        Assert.Equal(new MemoryInfo(16000 * 1024, 4000 * 1024), memory);
    }

    [Theory]
    [InlineData("Image=abcdef0123456789\n", "Image")]
    [InlineData("# Image=old\nImage=abcdef0123456789\n", "Image")]
    [InlineData("VibeMaker__Runtime__Image=\"abcdef0123456789\"\n", "VibeMaker__Runtime__Image")]
    public void PinnedImages_ReadsLastNonCommentValue(string content, string key) =>
        Assert.Equal("abcdef0123456789", PinnedImages.ReadValue(content, key));

    [Fact]
    public void PinnedImages_ResolveIdOrTag_AndFailClosed()
    {
        var images = new[]
        {
            new ImageFact("abcdef0123456789" + new string('0', 48), ["localhost/ymir/api:1"], 1, DateTimeOffset.UnixEpoch, new Dictionary<string, string>()),
            new ImageFact("1234567890ab" + new string('0', 52), ["localhost/ymir/agent-runtime:latest"], 1, DateTimeOffset.UnixEpoch, new Dictionary<string, string>()),
        };
        var store = new MaintenanceStoreOptions
        {
            Name = "root",
            PinnedImageSources = [new PinnedImageSource { File = "/q", Key = "Image" }, new PinnedImageSource { File = "/e", Key = "Img" }],
        };
        var files = new Dictionary<string, string> { ["/q"] = "Image=sha256:abcdef012345\n", ["/e"] = "Img=localhost/ymir/agent-runtime\n" };

        var pinned = PinnedImages.Resolve(store, images, p => files.GetValueOrDefault(p));
        Assert.Equal(2, pinned!.Count);

        files.Remove("/e");
        Assert.Null(PinnedImages.Resolve(store, images, p => files.GetValueOrDefault(p)));
        files["/e"] = "Other=x\n";
        Assert.Null(PinnedImages.Resolve(store, images, p => files.GetValueOrDefault(p)));
    }

    public static TheoryData<string> UnsafeOptions => new() { "weak-token", "no-stores", "duplicate-store", "tagged-repository", "wildcard-repository", "agents-without-root", "relative-pinned-file", "relative-state" };

    [Theory]
    [MemberData(nameof(UnsafeOptions))]
    public void Options_RejectUnsafeConfiguration(string problem)
    {
        var options = new MaintenanceHostOptions
        {
            Token = new string('t', 40),
            Stores = [new MaintenanceStoreOptions { Name = "root", ImageRepositories = ["localhost/ymir/api"] }],
        };
        options.Validate();

        var store = options.Stores[0];
        switch (problem)
        {
            case "weak-token": options.Token = "short"; break;
            case "no-stores": options.Stores.Clear(); break;
            case "duplicate-store": options.Stores.Add(new MaintenanceStoreOptions { Name = "root" }); break;
            case "tagged-repository": store.ImageRepositories = ["localhost/ymir/api:latest"]; break;
            case "wildcard-repository": store.ImageRepositories = ["localhost/*"]; break;
            case "agents-without-root": store.AgentContainers = true; break;
            case "relative-pinned-file": store.PinnedImageSources = [new PinnedImageSource { File = "etc/x", Key = "Image" }]; break;
            case "relative-state": options.StateDirectory = "state"; break;
        }

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Policy_Validation()
    {
        Assert.Null(ReclamationPolicy.Default.Validate());
        Assert.NotNull((ReclamationPolicy.Default with { ContainerRetentionDays = -1 }).Validate());
        Assert.NotNull((ReclamationPolicy.Default with { DiskMinAvailablePercent = 100 }).Validate());
        Assert.NotNull((ReclamationPolicy.Default with { MemoryMinAvailableGb = double.NaN }).Validate());
    }

    [Theory]
    [InlineData("unix:/run/ymir-maintenance/maintenance.sock", true)]
    [InlineData("http://127.0.0.1:5091", true)]
    [InlineData("http://0.0.0.0:5091", false)]
    [InlineData("http://example.com:5091", false)]
    [InlineData("unix:relative.sock", false)]
    [InlineData("unix:/run/../etc/x.sock", false)]
    public void Endpoint_OnlyUnixSocketOrLoopback(string value, bool valid)
    {
        if (valid)
        {
            MaintenanceEndpoint.Parse(value, "test");
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => MaintenanceEndpoint.Parse(value, "test"));
        }
    }
}
