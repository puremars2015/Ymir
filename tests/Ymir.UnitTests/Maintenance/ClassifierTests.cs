using Ymir.Maintenance;
using Ymir.MaintenanceHost;

namespace Ymir.UnitTests.Maintenance;

/// <summary>ADR-0025 §4 的保護規則：任何一項無法證明安全就保護（fail closed）。</summary>
public class ClassifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly string AgentName = $"ymir-user-{UserId:N}";

    private static MaintenanceStoreOptions Store(bool agents = true) => new()
    {
        Name = "ymir",
        Account = "ymir",
        ImageRepositories = ["localhost/ymir/api", "localhost/ymir/agent-runtime"],
        ServiceContainers = ["ymir-api", "ymir-sql-old"],
        AgentContainers = agents,
        AgentWorkspaceRoot = "/srv/ymir/workspaces",
    };

    private static ClassificationContext Context(
        ReclamationPolicy? policy = null,
        IReadOnlySet<string>? pinned = null,
        bool pinnedUnknown = false,
        string[]? registered = null,
        string[]? protectedImages = null,
        string[]? excluded = null,
        bool workspaceExists = true) => new(
            Store(),
            policy ?? ReclamationPolicy.Default with { Enabled = true, ContainerRetentionDays = 7, ImageRetentionDays = 7 },
            Now,
            pinnedUnknown ? null : pinned ?? new HashSet<string>(),
            registered ?? [],
            protectedImages ?? [],
            new HashSet<string>(excluded ?? [], StringComparer.Ordinal),
            3,
            _ => workspaceExists);

    private static ContainerFact Agent(string state = "exited", bool reclaimLabel = true, string mountType = "bind", string source = "/srv/ymir/workspaces/u1/workspace", string imageId = "img-agent")
    {
        var labels = new Dictionary<string, string> { ["ymir.user-id"] = UserId.ToString("D") };
        if (reclaimLabel)
        {
            labels["ymir.reclaimable"] = "agent-runtime";
        }

        return new ContainerFact("c" + new string('1', 63), AgentName, imageId, "localhost/ymir/agent-runtime:1", state, labels)
        {
            Mounts = [new MountFact(mountType, source, "/workspace")],
        };
    }

    private static StoreInventory Inventory(IReadOnlyList<ContainerFact> containers, IReadOnlyList<ImageFact>? images = null, bool psOk = true) =>
        new("ymir", Now, psOk, containers, true, images ?? [], "/var/lib/containers", null);

    private static ImageFact Image(string id, string[] tags, int ageDays = 30, bool managed = true) =>
        new(id, tags, 100, Now.AddDays(-ageDays), managed ? new Dictionary<string, string> { ["ymir.managed"] = "true" } : new Dictionary<string, string>());

    private static Dictionary<string, DateTimeOffset> Unused(string key, int daysAgo) => new() { [key] = Now.AddDays(-daysAgo) };

    [Theory]
    [InlineData("running")]
    [InlineData("paused")]
    [InlineData("created")]
    [InlineData("restarting")]
    [InlineData("initialized")]
    public void ActiveOrStartingAgentContainers_AreNeverReclaimed(string state)
    {
        var result = Classifier.ClassifyContainers(Inventory([Agent(state)]), Context(), new Dictionary<string, DateTimeOffset>());
        Assert.Equal(ProtectionReason.Active, Assert.Single(result).Protection);
    }

    [Fact]
    public void StoppedAgentWithLabelAndExternalWorkspace_IsEligible_AfterRetention()
    {
        var agent = Agent();
        var unused = Unused(ObservationKeys.Container("ymir", agent.Id), 8);

        var result = Assert.Single(Classifier.ClassifyContainers(Inventory([agent]), Context(), unused));

        Assert.Null(result.Protection);
        Assert.True(result.RetentionEligible);
        Assert.Equal(UserId, result.UserId);
    }

    [Fact]
    public void RetentionIsCountedFromObservedStop_NotReachedYet()
    {
        var agent = Agent();
        var result = Assert.Single(Classifier.ClassifyContainers(Inventory([agent]), Context(), Unused(ObservationKeys.Container("ymir", agent.Id), 6)));
        Assert.Null(result.Protection);
        Assert.False(result.RetentionEligible);
    }

    [Fact]
    public void ZeroRetentionDays_MeansNoAgeBasedReclaim_NotImmediate()
    {
        var agent = Agent();
        var policy = ReclamationPolicy.Default with { Enabled = true, ContainerRetentionDays = 0 };
        var result = Assert.Single(Classifier.ClassifyContainers(Inventory([agent]), Context(policy), Unused(ObservationKeys.Container("ymir", agent.Id), 365)));
        Assert.False(result.RetentionEligible);
    }

    [Fact]
    public void ContainerWithoutReclaimLabel_IsProtected()
    {
        var result = Assert.Single(Classifier.ClassifyContainers(Inventory([Agent(reclaimLabel: false)]), Context(), new Dictionary<string, DateTimeOffset>()));
        Assert.Equal(ProtectionReason.NotReclaimable, result.Protection);
    }

    [Theory]
    [InlineData("volume", "/srv/ymir/workspaces/u1/workspace", true)]
    [InlineData("bind", "/home/other/workspace", true)]
    [InlineData("bind", "/srv/ymir/workspaces-evil/u1", true)]
    [InlineData("bind", "/srv/ymir/workspaces/u1/workspace", false)]
    public void WorkspaceMustBeExternalAndAccessible(string type, string source, bool exists)
    {
        var result = Assert.Single(Classifier.ClassifyContainers(Inventory([Agent(mountType: type, source: source)]), Context(workspaceExists: exists), new Dictionary<string, DateTimeOffset>()));
        Assert.Equal(ProtectionReason.WorkspaceUnverified, result.Protection);
    }

    [Fact]
    public void ServiceAndExcludedContainers_AreNeverReclaimed()
    {
        var api = new ContainerFact("a1", "ymir-api", "img", "localhost/ymir/api:1", "exited", new Dictionary<string, string>()) { Mounts = [] };
        var old = new ContainerFact("a2", "ymir-sql-old", "img2", "mssql", "exited", new Dictionary<string, string>()) { Mounts = [] };

        var result = Classifier.ClassifyContainers(Inventory([api, old]), Context(excluded: ["ymir-sql-old"]), new Dictionary<string, DateTimeOffset>());

        Assert.Equal(ProtectionReason.ServiceContainer, result.Single(c => c.Fact.Name == "ymir-api").Protection);
        Assert.Equal(ProtectionReason.Excluded, result.Single(c => c.Fact.Name == "ymir-sql-old").Protection);
    }

    [Fact]
    public void OtherProjectsContainers_AreNotListed()
    {
        var foreign = new ContainerFact("f1", "gmshe_db", "img", "mssql", "exited", new Dictionary<string, string>());
        var spoofed = new ContainerFact("f2", AgentName, "img", "x", "exited", new Dictionary<string, string> { ["ymir.user-id"] = Guid.NewGuid().ToString() });

        Assert.Empty(Classifier.ClassifyContainers(Inventory([foreign, spoofed]), Context(), new Dictionary<string, DateTimeOffset>()));
    }

    [Fact]
    public void InspectFailure_FailsClosed()
    {
        var agent = Agent() with { Mounts = null };
        Assert.Equal(ProtectionReason.StateUnknown, Assert.Single(Classifier.ClassifyContainers(Inventory([agent]), Context(), new Dictionary<string, DateTimeOffset>())).Protection);
    }

    [Fact]
    public void ImagesReferencedByAnyContainer_IncludingStoppedOrForeign_AreProtected()
    {
        var images = Enumerable.Range(1, 5).Select(i => Image($"img{i}", [$"localhost/ymir/api:v{i}"], ageDays: 10 - i)).ToList();
        var stoppedForeign = new ContainerFact("x", "someone-else", "img1", "localhost/ymir/api:v1", "exited", new Dictionary<string, string>());

        var result = Classifier.ClassifyImages(Inventory([stoppedForeign], images), Context(), new Dictionary<string, DateTimeOffset>());

        Assert.Equal(ProtectionReason.Referenced, result.Single(i => i.Fact.Id == "img1").Protection);
    }

    [Fact]
    public void ThreeMostRecentVersions_PinnedAndProtected_AreKept()
    {
        var images = Enumerable.Range(1, 7).Select(i => Image($"img{i}", [$"localhost/ymir/api:v{i}"], ageDays: 10 - i)).ToList();

        var result = Classifier.ClassifyImages(Inventory([], images), Context(pinned: new HashSet<string> { "img1" }, protectedImages: ["img2" + new string('0', 10)]), new Dictionary<string, DateTimeOffset>())
            .ToDictionary(i => i.Fact.Id, i => i.Protection);

        Assert.Equal(ProtectionReason.Pinned, result["img1"]);
        Assert.Null(result["img2"]); // 登記值必須是至少 12 碼且相符的前綴
        Assert.Null(result["img3"]);
        Assert.Null(result["img4"]);
        Assert.Equal(ProtectionReason.RecentVersion, result["img5"]);
        Assert.Equal(ProtectionReason.RecentVersion, result["img6"]);
        Assert.Equal(ProtectionReason.RecentVersion, result["img7"]);
    }

    [Fact]
    public void ExplicitlyProtectedVersion_IsKept()
    {
        var id = "abcdef0123456789" + new string('0', 48);
        var images = new[] { Image(id, ["localhost/ymir/api:old"], 100) }.Concat(Enumerable.Range(1, 3).Select(i => Image($"new{i}", [$"localhost/ymir/api:n{i}"], 1))).ToList();

        var result = Classifier.ClassifyImages(Inventory([], images), Context(protectedImages: ["abcdef012345"]), new Dictionary<string, DateTimeOffset>());

        Assert.Equal(ProtectionReason.ProtectedVersion, result.Single(i => i.Fact.Id == id).Protection);
    }

    [Fact]
    public void ImageWithForeignTag_IsProtected()
    {
        var images = new[] { Image("shared", ["localhost/ymir/api:v1", "docker.io/other/app:latest"]) }.Concat(Enumerable.Range(1, 3).Select(i => Image($"n{i}", [$"localhost/ymir/api:n{i}"], 1))).ToList();
        var result = Classifier.ClassifyImages(Inventory([], images), Context(), new Dictionary<string, DateTimeOffset>());
        Assert.Equal(ProtectionReason.ForeignTag, result.Single(i => i.Fact.Id == "shared").Protection);
    }

    [Fact]
    public void ImageWithoutManagedLabelOrRegistration_IsUnproven_EvenWithYmirName()
    {
        var images = new[] { Image("legacy00000000", ["localhost/ymir/api:legacy"], 100, managed: false) }.Concat(Enumerable.Range(1, 3).Select(i => Image($"n{i}", [$"localhost/ymir/api:n{i}"], 1))).ToList();

        var unproven = Classifier.ClassifyImages(Inventory([], images), Context(), new Dictionary<string, DateTimeOffset>());
        var registered = Classifier.ClassifyImages(Inventory([], images), Context(registered: ["legacy000000"]), new Dictionary<string, DateTimeOffset>());

        Assert.Equal(ProtectionReason.OwnershipUnproven, unproven.Single(i => i.Fact.Id == "legacy00000000").Protection);
        Assert.Null(registered.Single(i => i.Fact.Id == "legacy00000000").Protection);
    }

    [Fact]
    public void DanglingImages_OfUnknownOwnership_AreSkipped_ManagedOnesConsidered()
    {
        var images = new List<ImageFact> { Image("unknown-dangling", [], managed: false), Image("ymir-stage", []) };
        var result = Classifier.ClassifyImages(Inventory([], images), Context(), new Dictionary<string, DateTimeOffset>());

        var only = Assert.Single(result);
        Assert.Equal("ymir-stage", only.Fact.Id);
        Assert.Null(only.Protection);
    }

    [Fact]
    public void ContainerQueryFailure_ProtectsEveryImage()
    {
        var images = Enumerable.Range(1, 5).Select(i => Image($"img{i}", [$"localhost/ymir/api:v{i}"], 10 - i)).ToList();
        var result = Classifier.ClassifyImages(Inventory([], images, psOk: false), Context(), new Dictionary<string, DateTimeOffset>());
        Assert.All(result, i => Assert.Equal(ProtectionReason.StateUnknown, i.Protection));
    }

    [Fact]
    public void UnreadablePinnedConfiguration_ProtectsEveryImage()
    {
        var images = Enumerable.Range(1, 5).Select(i => Image($"img{i}", [$"localhost/ymir/api:v{i}"], 10 - i)).ToList();
        var result = Classifier.ClassifyImages(Inventory([], images), Context(pinnedUnknown: true), new Dictionary<string, DateTimeOffset>());
        Assert.All(result, i => Assert.Equal(ProtectionReason.StateUnknown, i.Protection));
    }

    [Fact]
    public void SharedLayers_AreFlagged()
    {
        var a = Image("a", ["localhost/ymir/api:a"]) with { Layers = ["base", "a1"] };
        var b = Image("b", ["localhost/ymir/api:b"]) with { Layers = ["base", "b1"] };
        var c = Image("c", ["localhost/ymir/agent-runtime:c"]) with { Layers = ["c1"] };

        var result = Classifier.ClassifyImages(Inventory([], [a, b, c]), Context(), new Dictionary<string, DateTimeOffset>()).ToDictionary(i => i.Fact.Id, i => i.HasSharedLayers);

        Assert.True(result["a"]);
        Assert.True(result["b"]);
        Assert.False(result["c"]);
    }

    [Theory]
    [InlineData("localhost/ymir/api:abc", "localhost/ymir/api")]
    [InlineData("registry:5000/ymir/api:1", "registry:5000/ymir/api")]
    [InlineData("registry:5000/ymir/api", "registry:5000/ymir/api")]
    public void RepositoryOf_HandlesRegistryPorts(string tag, string repository) => Assert.Equal(repository, Classifier.RepositoryOf(tag));
}
