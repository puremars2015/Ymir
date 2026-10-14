using System.Text.Json;

namespace Ymir.MaintenanceHost;

/// <summary>以 container CLI 盤點一個 store。每個查詢各自處理失敗：失敗的部分標記為不可用，而不是當作空的（ADR-0025 §1）。</summary>
internal sealed class StoreScanner(IContainerCli cli, ILogger<StoreScanner> logger)
{
    public async Task<StoreInventory> ScanAsync(MaintenanceStoreOptions store, IReadOnlyCollection<string> registered, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var graphRoot = await QueryAsync(store, ["info", "--format", "{{.Store.GraphRoot}}"], text => text.Trim(), "info", errors, cancellationToken).ConfigureAwait(false);
        var containers = await QueryAsync(store, ["ps", "--all", "--no-trunc", "--format", "json"], PodmanJson.ParseContainers, "ps", errors, cancellationToken).ConfigureAwait(false);
        var images = await QueryAsync(store, ["images", "--all", "--no-trunc", "--format", "json"], PodmanJson.ParseImages, "images", errors, cancellationToken).ConfigureAwait(false);

        var detailed = containers is null ? null : await DescribeContainersAsync(store, containers, errors, cancellationToken).ConfigureAwait(false);
        var layered = images is null ? null : await AddLayersAsync(store, images, registered, errors, cancellationToken).ConfigureAwait(false);

        return new StoreInventory(
            store.Name!,
            now,
            detailed is not null,
            detailed ?? [],
            layered is not null,
            layered ?? [],
            string.IsNullOrEmpty(graphRoot) ? null : graphRoot,
            errors.Count == 0 ? null : string.Join("、", errors));
    }

    /// <summary>只為 Ymir 的 container 取健康狀態、掛載與 CPU / 記憶體；inspect 失敗時 Mounts 維持 null（之後分類為 StateUnknown）。</summary>
    private async Task<IReadOnlyList<ContainerFact>> DescribeContainersAsync(MaintenanceStoreOptions store, IReadOnlyList<ContainerFact> containers, List<string> errors, CancellationToken cancellationToken)
    {
        var ymir = containers.Where(c => Classifier.IsYmirContainer(store, c, out _, out _)).Select(c => c.Id).ToList();
        if (ymir.Count == 0)
        {
            return containers;
        }

        var inspected = await QueryAsync(store, ["container", "inspect", "--format", "json", .. ymir], PodmanJson.ParseContainerInspect, "container inspect", errors, cancellationToken).ConfigureAwait(false);
        var running = containers.Where(c => ymir.Contains(c.Id) && c.State == "running").Select(c => c.Id).ToList();
        var stats = running.Count == 0
            ? null
            : await QueryAsync(store, ["stats", "--no-stream", "--no-reset", "--format", "json", .. running], PodmanJson.ParseStats, "stats", errors, cancellationToken).ConfigureAwait(false);

        return [.. containers.Select(c =>
        {
            if (!ymir.Contains(c.Id))
            {
                return c;
            }

            var detail = inspected is not null && inspected.TryGetValue(c.Id, out var d) ? d : ((string?, IReadOnlyList<MountFact>, string)?)null;
            var usage = stats is not null && stats.TryGetValue(c.Id[..Math.Min(12, c.Id.Length)], out var u) ? u : ((double?, long?)?)null;
            return c with
            {
                // inspect 的狀態比 ps 新；兩者不一致時以 inspect 為準。
                State = detail?.Item3 ?? c.State,
                Health = detail?.Item1,
                Mounts = detail?.Item2,
                CpuPercent = usage?.Item1,
                MemoryBytes = usage?.Item2,
            };
        })];
    }

    /// <summary>
    /// 取所有 image 的 layer，用來判斷 Ymir image 是否與其他 image 共用 layer。只是顯示用途：失敗時 layer 為空（顯示為共用），不影響保護判斷。
    /// </summary>
    private async Task<IReadOnlyList<ImageFact>> AddLayersAsync(MaintenanceStoreOptions store, IReadOnlyList<ImageFact> images, IReadOnlyCollection<string> registered, List<string> errors, CancellationToken cancellationToken)
    {
        if (!images.Any(i => Classifier.IsYmirImage(store, i, registered)))
        {
            return images;
        }

        var layers = await QueryAsync(store, ["image", "inspect", "--format", "json", .. images.Select(i => i.Id)], PodmanJson.ParseImageLayers, "image inspect", errors, cancellationToken).ConfigureAwait(false);
        return layers is null ? images : [.. images.Select(i => layers.TryGetValue(i.Id, out var l) ? i with { Layers = l } : i)];
    }

    private async Task<T?> QueryAsync<T>(MaintenanceStoreOptions store, IReadOnlyList<string> arguments, Func<string, T> parse, string name, List<string> errors, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var result = await cli.RunAsync(store, arguments, cancellationToken).ConfigureAwait(false);
            if (result.Succeeded)
            {
                return parse(result.Output);
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            logger.LogWarning(ex, "Maintenance query {Query} failed for store {Store}", name, store.Name);
        }

        errors.Add($"{name} 查詢失敗");
        return null;
    }
}
