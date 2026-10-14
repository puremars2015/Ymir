using System.Text.RegularExpressions;
using Ymir.Maintenance;

namespace Ymir.MaintenanceHost;

/// <summary>分類後的 container（含內部用的使用者 id）。</summary>
internal sealed record ClassifiedContainer(ContainerFact Fact, string Store, bool IsAgentRuntime, Guid? UserId, DateTimeOffset? UnusedSince, ProtectionReason? Protection, bool RetentionEligible)
{
    public ContainerResource ToResource() => new(
        Store, Fact.Name, Fact.Id[..Math.Min(12, Fact.Id.Length)], Fact.ImageName, IsAgentRuntime, Fact.State, Fact.Health,
        Fact.CpuPercent, Fact.MemoryBytes, UnusedSince, Protection, RetentionEligible);
}

internal sealed record ClassifiedImage(ImageFact Fact, string Store, int ContainerReferences, bool HasSharedLayers, DateTimeOffset? UnusedSince, ProtectionReason? Protection, bool RetentionEligible)
{
    public ImageResource ToResource() => new(
        Store, Fact.Id[..Math.Min(12, Fact.Id.Length)], Fact.Tags, Fact.SizeBytes, HasSharedLayers, ContainerReferences, Fact.Created,
        UnusedSince, Protection, RetentionEligible);
}

/// <summary>分類需要的輸入；<see cref="PinnedImageIds"/> 為 null 代表正式設定讀取失敗（fail closed）。</summary>
internal sealed record ClassificationContext(
    MaintenanceStoreOptions Store,
    ReclamationPolicy Policy,
    DateTimeOffset Now,
    IReadOnlySet<string>? PinnedImageIds,
    IReadOnlyCollection<string> RegisteredImages,
    IReadOnlyCollection<string> ProtectedImages,
    IReadOnlySet<string> ExcludedContainers,
    int KeepRecentVersions,
    Func<string, bool> DirectoryExists);

/// <summary>
/// 回收保護規則（ADR-0025 §4），全部是純函式：同樣的盤點與觀測時間一定得到同樣的結果。
/// 任何一項無法證明安全就保護（fail closed）；只有 <see cref="ProtectionReason"/> 為 null 的項目才可能被刪除。
/// </summary>
internal static partial class Classifier
{
    public const string ManagedLabel = "ymir.managed";

    public const string ReclaimableLabel = "ymir.reclaimable";

    public const string AgentRuntimeReclaimValue = "agent-runtime";

    public const string UserIdLabel = "ymir.user-id";

    public const string WorkspaceMountTarget = "/workspace";

    /// <summary>可視為「停止」而開始計算未使用時間、可回收的 container 狀態；其他狀態（running、paused、created、restarting…）一律保護。</summary>
    public static bool IsStopped(string state) => state is "exited" or "stopped";

    /// <summary>
    /// 是否為這個 store 中要顯示的 Ymir container：部署設定列出的服務 container，或（AgentContainers 開啟時）名稱與 user-id label 一致的 Agent runtime。
    /// </summary>
    public static bool IsYmirContainer(MaintenanceStoreOptions store, ContainerFact container, out bool isAgent, out Guid? userId)
    {
        isAgent = false;
        userId = null;
        if (store.ServiceContainers.Contains(container.Name, StringComparer.Ordinal))
        {
            return true;
        }

        if (!store.AgentContainers)
        {
            return false;
        }

        var match = AgentContainerName().Match(container.Name);
        if (!match.Success
            || !Guid.TryParseExact(match.Groups[1].Value, "N", out var nameId)
            || !container.Labels.TryGetValue(UserIdLabel, out var labelValue)
            || !Guid.TryParse(labelValue, out var labelId)
            || labelId != nameId)
        {
            return false;
        }

        isAgent = true;
        userId = nameId;
        return true;
    }

    /// <summary>image 的 repository（去掉 tag；保留含 port 的 registry 主機名稱）。</summary>
    public static string RepositoryOf(string tag)
    {
        var slash = tag.LastIndexOf('/');
        var colon = tag.LastIndexOf(':');
        return colon > slash ? tag[..colon] : tag;
    }

    /// <summary>是否顯示這個 image：有 Ymir repository 的 tag，或 dangling 但可證明是 Ymir 的（管理 label 或已登記）。歸屬不明的 dangling 一律略過。</summary>
    public static bool IsYmirImage(MaintenanceStoreOptions store, ImageFact image, IReadOnlyCollection<string> registered) =>
        image.IsDangling
            ? IsManaged(image) || IsRegistered(image.Id, registered)
            : image.Tags.Any(t => store.ImageRepositories.Contains(RepositoryOf(t), StringComparer.Ordinal));

    public static IReadOnlyList<ClassifiedContainer> ClassifyContainers(
        StoreInventory inventory,
        ClassificationContext context,
        IReadOnlyDictionary<string, DateTimeOffset> unusedSince)
    {
        var result = new List<ClassifiedContainer>();
        if (!inventory.ContainersAvailable)
        {
            return result;
        }

        foreach (var container in inventory.AllContainers)
        {
            if (!IsYmirContainer(context.Store, container, out var isAgent, out var userId))
            {
                continue;
            }

            var since = IsStopped(container.State) && unusedSince.TryGetValue(ObservationKeys.Container(inventory.Store, container.Id), out var s) ? s : (DateTimeOffset?)null;
            var protection = ContainerProtection(container, isAgent, context);
            var retention = protection is null
                && context.Policy.ContainerRetentionDays > 0
                && since is { } stoppedSince
                && context.Now - stoppedSince >= TimeSpan.FromDays(context.Policy.ContainerRetentionDays);
            result.Add(new ClassifiedContainer(container, inventory.Store, isAgent, userId, since, protection, retention));
        }

        return result;
    }

    public static ProtectionReason? ContainerProtection(ContainerFact container, bool isAgent, ClassificationContext context)
    {
        if (context.ExcludedContainers.Contains(container.Name))
        {
            return ProtectionReason.Excluded;
        }

        if (!isAgent)
        {
            return ProtectionReason.ServiceContainer;
        }

        if (container.State == "unknown" || container.Mounts is null)
        {
            return ProtectionReason.StateUnknown;
        }

        if (!IsStopped(container.State))
        {
            return ProtectionReason.Active;
        }

        if (!container.Labels.TryGetValue(ReclaimableLabel, out var reclaimable) || reclaimable != AgentRuntimeReclaimValue)
        {
            return ProtectionReason.NotReclaimable;
        }

        return WorkspaceIsExternal(container.Mounts, context) ? null : ProtectionReason.WorkspaceUnverified;
    }

    /// <summary>workspace 必須是外置的 bind mount、位於部署設定的 workspace 根目錄下，且目前仍可存取（ADR-0025 §4）。</summary>
    public static bool WorkspaceIsExternal(IReadOnlyList<MountFact> mounts, ClassificationContext context)
    {
        if (context.Store.AgentWorkspaceRoot is not { } root)
        {
            return false;
        }

        var workspace = mounts.Where(m => m.Destination == WorkspaceMountTarget).ToList();
        if (workspace.Count != 1 || workspace[0].Type != "bind" || !Path.IsPathFullyQualified(workspace[0].Source))
        {
            return false;
        }

        var source = Path.GetFullPath(workspace[0].Source);
        var normalizedRoot = Path.GetFullPath(root).TrimEnd('/') + "/";
        return source.StartsWith(normalizedRoot, StringComparison.Ordinal) && context.DirectoryExists(source);
    }

    public static IReadOnlyList<ClassifiedImage> ClassifyImages(
        StoreInventory inventory,
        ClassificationContext context,
        IReadOnlyDictionary<string, DateTimeOffset> unusedSince)
    {
        var result = new List<ClassifiedImage>();
        if (!inventory.ImagesAvailable)
        {
            return result;
        }

        var references = inventory.AllContainers.GroupBy(c => c.ImageId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var layerOwners = inventory.AllImages
            .SelectMany(i => i.Layers.Distinct(StringComparer.Ordinal).Select(layer => (layer, i.Id)))
            .GroupBy(x => x.layer, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var ymirImages = inventory.AllImages.Where(i => IsYmirImage(context.Store, i, context.RegisteredImages)).ToList();
        var recent = RecentVersions(ymirImages, context);

        foreach (var image in ymirImages)
        {
            var refs = references.GetValueOrDefault(image.Id);
            // layer 資訊取不到時保守地當作有共用（大小不等於可釋放空間）。
            var shared = image.Layers.Count == 0 || image.Layers.Any(l => layerOwners.GetValueOrDefault(l) > 1);
            var since = inventory.ContainersAvailable && refs == 0 && unusedSince.TryGetValue(ObservationKeys.Image(inventory.Store, image.Id), out var s) ? s : (DateTimeOffset?)null;
            var protection = ImageProtection(image, refs, inventory.ContainersAvailable, recent.Contains(image.Id), context);
            var retention = protection is null
                && context.Policy.ImageRetentionDays > 0
                && since is { } unused
                && context.Now - unused >= TimeSpan.FromDays(context.Policy.ImageRetentionDays);
            result.Add(new ClassifiedImage(image, inventory.Store, refs, shared, since, protection, retention));
        }

        return result;
    }

    public static ProtectionReason? ImageProtection(ImageFact image, int references, bool containersAvailable, bool isRecent, ClassificationContext context)
    {
        if (!containersAvailable || context.PinnedImageIds is null)
        {
            return ProtectionReason.StateUnknown;
        }

        if (image.Tags.Any(t => !context.Store.ImageRepositories.Contains(RepositoryOf(t), StringComparer.Ordinal)))
        {
            return ProtectionReason.ForeignTag;
        }

        if (!IsManaged(image) && !IsRegistered(image.Id, context.RegisteredImages))
        {
            return ProtectionReason.OwnershipUnproven;
        }

        if (references > 0)
        {
            return ProtectionReason.Referenced;
        }

        if (context.PinnedImageIds.Contains(image.Id))
        {
            return ProtectionReason.Pinned;
        }

        if (IsRegistered(image.Id, context.ProtectedImages))
        {
            return ProtectionReason.ProtectedVersion;
        }

        return isRecent ? ProtectionReason.RecentVersion : null;
    }

    /// <summary>每個 Ymir repository 依建立時間保留最新的 N 個不同 image（dangling 不算版本）。</summary>
    public static IReadOnlySet<string> RecentVersions(IReadOnlyList<ImageFact> images, ClassificationContext context)
    {
        var keep = new HashSet<string>(StringComparer.Ordinal);
        foreach (var repository in context.Store.ImageRepositories)
        {
            foreach (var image in images
                         .Where(i => i.Tags.Any(t => RepositoryOf(t) == repository))
                         .OrderByDescending(i => i.Created)
                         .ThenBy(i => i.Id, StringComparer.Ordinal)
                         .Take(context.KeepRecentVersions))
            {
                keep.Add(image.Id);
            }
        }

        return keep;
    }

    public static bool IsManaged(ImageFact image) => image.Labels.TryGetValue(ManagedLabel, out var value) && value == "true";

    /// <summary>登記值是完整 ID 或至少 12 碼的前綴。</summary>
    public static bool IsRegistered(string imageId, IReadOnlyCollection<string> registered) =>
        registered.Any(r => PodmanJson.NormalizeId(r.Trim()) is { Length: >= 12 } prefix && imageId.StartsWith(prefix, StringComparison.Ordinal));

    [GeneratedRegex("^ymir-user-([0-9a-f]{32})$", RegexOptions.CultureInvariant)]
    private static partial Regex AgentContainerName();
}

internal static class ObservationKeys
{
    public static string ContainerScope(string store) => $"{store}/container/";

    public static string ImageScope(string store) => $"{store}/image/";

    public static string Container(string store, string id) => ContainerScope(store) + id;

    public static string Image(string store, string id) => ImageScope(store) + id;
}
