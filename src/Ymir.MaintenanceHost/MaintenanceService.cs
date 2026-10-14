using Ymir.Maintenance;

namespace Ymir.MaintenanceHost;

internal sealed record StoreSample(
    MaintenanceStoreOptions Options,
    StoreInventory Inventory,
    FilesystemInfo? Filesystem,
    IReadOnlyList<ClassifiedContainer> Containers,
    IReadOnlyList<ClassifiedImage> Images);

internal sealed record Sample(DateTimeOffset At, HostResources Host, IReadOnlyList<FilesystemInfo> Filesystems, IReadOnlyList<StoreSample> Stores);

/// <summary>可刪除的候選（內部用）。</summary>
internal sealed record Candidate(string Store, bool IsContainer, string Id, string Name, DateTimeOffset? UnusedSince, Guid? UserId, IReadOnlyList<string> Tags);

/// <summary>
/// 主機資源監控與安全自動回收（ADR-0025）。採樣、dry run 與回收以同一個 gate 序列化，不會同時刪除。
/// 每個刪除前重新盤點並重新分類該項目，持有生命週期鎖，且不使用強制刪除（ADR-0025 §4）。
/// </summary>
internal sealed class MaintenanceService(
    MaintenanceHostOptions options,
    IContainerCli cli,
    StoreScanner scanner,
    IHostMetricsSource host,
    MaintenanceStateStore state,
    TimeProvider timeProvider,
    ILogger<MaintenanceService> logger) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly PressureTracker _pressure = new(TimeSpan.FromMinutes(options.SustainMinutes), TimeSpan.FromMinutes(options.CooldownMinutes));
    private readonly HashSet<string> _excluded = new(options.ExcludedContainers, StringComparer.Ordinal);
    private CpuTimes? _lastCpu;
    private Sample? _latest;

    public ReclamationPolicy Policy => state.State.Policy ?? ReclamationPolicy.Default;

    public bool LocksAvailable => options.LockDirectory is { } directory && host.DirectoryExists(directory);

    public ReclaimState ReclaimState => !Policy.Enabled ? ReclaimState.Disabled : LocksAvailable ? ReclaimState.Active : ReclaimState.LocksUnavailable;

    public void SetPolicy(ReclamationPolicy policy) => state.SetPolicy(policy);

    public void Dispose() => _gate.Dispose();

    public IReadOnlyList<ReclaimRun> GetRuns(long afterId) => state.GetRuns(afterId);

    /// <summary>採樣一次：主機資源、各 store 盤點、未使用時間與門檻狀態。回傳需要緊急回收的門檻。</summary>
    public async Task<IReadOnlyList<PressureReading>> SampleAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sample = await TakeSampleAsync(observe: true, cancellationToken).ConfigureAwait(false);
            _latest = sample;
            var readings = PressureTracker.Evaluate(Policy, options.RecoveryMarginPercent, sample.Host, ToFilesystemResources(sample));
            return _pressure.Update(readings, sample.At);
        }
        finally
        {
            _gate.Release();
        }
    }

    public ResourceSnapshot GetSnapshot()
    {
        var now = timeProvider.GetUtcNow();
        var sample = _latest;
        if (sample is null)
        {
            return new ResourceSnapshot(
                now,
                new HostResources(DataStatus.Failed, null, host.CpuCount, null, null, 0, 0, 0),
                [],
                [.. options.Stores.Select(s => new StoreStatus(s.Name!, DataStatus.Failed, null, null, "尚未完成第一次採樣"))],
                [],
                [],
                [],
                ReclaimState,
                options.ToTiming());
        }

        // 超過 3 個採樣間隔沒有更新：標示為過期，不當作目前狀態。
        var stale = now - sample.At > TimeSpan.FromSeconds(options.SampleIntervalSeconds * 3);
        return new ResourceSnapshot(
            now,
            stale && sample.Host.Status == DataStatus.Available ? sample.Host with { Status = DataStatus.Stale } : sample.Host,
            ToFilesystemResources(sample),
            [.. sample.Stores.Select(s => ToStoreStatus(s, stale))],
            [.. sample.Stores.SelectMany(s => s.Containers).Select(c => c.ToResource())],
            [.. sample.Stores.SelectMany(s => s.Images).Select(i => i.ToResource())],
            _pressure.Snapshot(),
            ReclaimState,
            options.ToTiming());
    }

    /// <summary>不刪除：目前一般回收會刪除的項目，與緊急回收可考慮的安全候選。</summary>
    public async Task<DryRunResult> DryRunAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sample = await TakeSampleAsync(observe: false, cancellationToken).ConfigureAwait(false);
            var notes = new List<string>();
            notes.AddRange(ReclaimState switch
            {
                ReclaimState.Disabled => ["自動回收未開啟：以下只是目前規則下的候選，不會被刪除。"],
                ReclaimState.LocksUnavailable => ["生命週期鎖無法使用：只提供監控與 dry run，不會自動刪除。"],
                _ => [],
            });
            notes.AddRange(sample.Stores.Where(s => s.Inventory.Error is not null).Select(s => $"{s.Inventory.Store}：{s.Inventory.Error}，該部分一律保護。"));
            notes.Add("停止的 container 與 image 通常不占用 CPU／記憶體；回收不保證降低負載。");
            return new DryRunResult(
                sample.At,
                ReclaimState,
                [.. Candidates(sample, retentionOnly: true, store: null).Select(c => ToItem(c, ReclaimItemResult.Skipped, "dry run"))],
                [.. Candidates(sample, retentionOnly: false, store: null).Select(c => ToItem(c, ReclaimItemResult.Skipped, "dry run"))],
                notes);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>依保留天數回收；只在自動回收運作中、有設定保留天數且距離上次超過檢查間隔時執行。</summary>
    public async Task<ReclaimRun?> RunRetentionSweepIfDueAsync(CancellationToken cancellationToken)
    {
        var policy = Policy;
        if (ReclaimState != ReclaimState.Active || (policy.ContainerRetentionDays == 0 && policy.ImageRetentionDays == 0))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        if (state.State.LastRetentionSweep is { } last && now - last < TimeSpan.FromMinutes(options.RetentionSweepMinutes))
        {
            return null;
        }

        state.SetLastRetentionSweep(now);
        return await RunRoundAsync(ReclaimTrigger.Retention, filesystemId: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>緊急回收（門檻持續不足）：略過一般保留天數，但不略過任何保護規則。</summary>
    public async Task<ReclaimRun?> RunPressureRoundAsync(PressureReading reading, CancellationToken cancellationToken)
    {
        if (ReclaimState != ReclaimState.Active)
        {
            return null;
        }

        var filesystemId = reading.Trigger == ReclaimTrigger.Disk ? reading.Key["disk:".Length..] : null;
        return await RunRoundAsync(reading.Trigger, filesystemId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ReclaimRun> RunRoundAsync(ReclaimTrigger trigger, string? filesystemId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var startedAt = timeProvider.GetUtcNow();
            var sample = await TakeSampleAsync(observe: true, cancellationToken).ConfigureAwait(false);
            _latest = sample;

            // 磁碟壓力只看位於受壓檔案系統上的 store；CPU / 記憶體壓力與保留天數看全部。
            var inScope = sample.Stores.Where(s => filesystemId is null || s.Filesystem?.Id == filesystemId).ToList();
            var candidates = inScope.SelectMany(s => Candidates(sample, trigger == ReclaimTrigger.Retention, s.Options.Name)).ToList();
            var protectedCount = inScope.Sum(s => s.Containers.Count(c => c.Protection is not null) + s.Images.Count(i => i.Protection is not null));
            var before = sample.Filesystems.Where(f => filesystemId is null || f.Id == filesystemId).ToDictionary(f => f.Id, f => f.AvailableBytes, StringComparer.Ordinal);

            var items = new List<ReclaimItem>();
            IDisposable? deployLock = null;
            var deployBusy = false;
            try
            {
                foreach (var candidate in candidates)
                {
                    if (items.Count(i => i.Result == ReclaimItemResult.Deleted) >= options.MaxDeletionsPerRound)
                    {
                        break;
                    }

                    if (!candidate.IsContainer && deployLock is null && !deployBusy)
                    {
                        deployLock = TryLock(LifecycleLock.DeployLockFileName);
                        deployBusy = deployLock is null;
                    }

                    if (!candidate.IsContainer && deployBusy)
                    {
                        items.Add(ToItem(candidate, ReclaimItemResult.Skipped, "部署進行中或部署鎖無法取得"));
                        continue;
                    }

                    items.Add(await DeleteAsync(candidate, trigger, cancellationToken).ConfigureAwait(false));

                    // 磁碟壓力：每刪一個就重新量測受壓的檔案系統，回到回復門檻就停止（不以 image 大小推算）。
                    if (filesystemId is not null && sample.Stores.FirstOrDefault(s => s.Options.Name == candidate.Store)?.Options is { } storeOptions
                        && await ResolveFilesystemAsync(storeOptions, cancellationToken).ConfigureAwait(false) is { } fs
                        && PressureTracker.Capacity(fs.AvailableBytes, fs.AvailablePercent, Policy.DiskMinAvailableGb, Policy.DiskMinAvailablePercent, options.RecoveryMarginPercent).Recovered)
                    {
                        break;
                    }
                }
            }
            finally
            {
                deployLock?.Dispose();
            }

            var after = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var store in inScope)
            {
                if (await ResolveFilesystemAsync(store.Options, cancellationToken).ConfigureAwait(false) is { } fs && before.ContainsKey(fs.Id))
                {
                    after[fs.Id] = fs.AvailableBytes;
                }
            }

            var deleted = items.Count(i => i.Result == ReclaimItemResult.Deleted);
            var failed = items.Count(i => i.Result == ReclaimItemResult.Failed);
            var deltas = before.Select(b => new FilesystemDelta(b.Key, b.Value, after.GetValueOrDefault(b.Key, b.Value))).ToList();
            var summary = Summarize(trigger, candidates.Count, deleted, failed, deltas);
            var run = state.AddRun(id => new ReclaimRun(id, startedAt, timeProvider.GetUtcNow(), trigger, candidates.Count, protectedCount, deleted, failed, deltas, items, summary));
            state.Save();
            logger.LogInformation("Reclaim run {RunId} ({Trigger}): {Summary}", run.Id, trigger, summary);
            return run;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>刪除前重新盤點並重新分類；container 持有該使用者的生命週期鎖並再次確認狀態；不使用 --force。</summary>
    private async Task<ReclaimItem> DeleteAsync(Candidate candidate, ReclaimTrigger trigger, CancellationToken cancellationToken)
    {
        var store = options.Stores.Single(s => s.Name == candidate.Store);
        var fresh = await SampleStoreAsync(store, timeProvider.GetUtcNow(), observe: false, cancellationToken).ConfigureAwait(false);
        var stillEligible = candidate.IsContainer
            ? fresh.Containers.FirstOrDefault(c => c.Fact.Id == candidate.Id) is { Protection: null } c && (trigger != ReclaimTrigger.Retention || c.RetentionEligible)
            : fresh.Images.FirstOrDefault(i => i.Fact.Id == candidate.Id) is { Protection: null } i && (trigger != ReclaimTrigger.Retention || i.RetentionEligible);
        if (!stillEligible)
        {
            return ToItem(candidate, ReclaimItemResult.Skipped, "重新確認後已不符合回收條件");
        }

        try
        {
            if (candidate.IsContainer)
            {
                using var userLock = TryLock(LifecycleLock.UserLockFileName(candidate.UserId!.Value));
                if (userLock is null)
                {
                    return ToItem(candidate, ReclaimItemResult.Skipped, "執行環境正在建立或啟動");
                }

                var inspect = await cli.RunAsync(store, ["container", "inspect", "--format", "{{.State.Status}}", candidate.Id], cancellationToken).ConfigureAwait(false);
                if (!inspect.Succeeded || !Classifier.IsStopped(inspect.Output.Trim()))
                {
                    return ToItem(candidate, ReclaimItemResult.Skipped, "狀態已改變");
                }

                var removed = await cli.RunAsync(store, ["rm", candidate.Id], cancellationToken).ConfigureAwait(false);
                return ToItem(candidate, removed.Succeeded ? ReclaimItemResult.Deleted : ReclaimItemResult.Failed, removed.Succeeded ? null : "刪除失敗");
            }

            // 有 tag 時逐一移除 tag（最後一個 tag 移除時 image 才刪除）；dangling 以 ID 刪除。都不加 --force。
            string[] targets = candidate.Tags.Count > 0 ? [.. candidate.Tags] : [candidate.Id];
            var result = await cli.RunAsync(store, ["rmi", .. targets], cancellationToken).ConfigureAwait(false);
            return ToItem(candidate, result.Succeeded ? ReclaimItemResult.Deleted : ReclaimItemResult.Failed, result.Succeeded ? null : "刪除失敗（可能仍被使用）");
        }
        catch (LifecycleLockUnavailableException)
        {
            return ToItem(candidate, ReclaimItemResult.Skipped, "生命週期鎖無法使用");
        }
    }

    private IDisposable? TryLock(string fileName)
    {
        try
        {
            return options.LockDirectory is { } directory ? LifecycleLock.TryAcquire(directory, fileName) : null;
        }
        catch (LifecycleLockUnavailableException)
        {
            return null;
        }
    }

    /// <summary>安全候選：沒有任何保護；依保留天數回收時另需超過保留天數。container 優先，各自依未使用時間由舊到新。</summary>
    private static IEnumerable<Candidate> Candidates(Sample sample, bool retentionOnly, string? store) =>
        sample.Stores
            .Where(s => store is null || s.Options.Name == store)
            .SelectMany(s =>
                s.Containers.Where(c => c.Protection is null && (!retentionOnly || c.RetentionEligible))
                    .Select(c => new Candidate(s.Options.Name!, true, c.Fact.Id, c.Fact.Name, c.UnusedSince, c.UserId, []))
                    .Concat(s.Images.Where(i => i.Protection is null && (!retentionOnly || i.RetentionEligible))
                        .Select(i => new Candidate(s.Options.Name!, false, i.Fact.Id, (i.Fact.Tags.Count > 0 ? i.Fact.Tags[0] : i.Fact.Id[..12]), i.UnusedSince, null, i.Fact.Tags))))
            .OrderBy(c => c.IsContainer ? 0 : 1)
            .ThenBy(c => c.UnusedSince ?? DateTimeOffset.MaxValue);

    private async Task<Sample> TakeSampleAsync(bool observe, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var cpu = host.ReadCpuTimes();
        var cpuUsed = HostMetrics.CpuUsedPercent(_lastCpu, cpu);
        if (cpu is not null)
        {
            _lastCpu = cpu;
        }

        var stores = new List<StoreSample>();
        foreach (var store in options.Stores)
        {
            stores.Add(await SampleStoreAsync(store, now, observe, cancellationToken).ConfigureAwait(false));
        }

        if (observe)
        {
            state.Save();
        }

        var filesystems = stores.Select(s => s.Filesystem).OfType<FilesystemInfo>().DistinctBy(f => f.MountPoint).ToList();
        return new Sample(now, HostMetrics.ToHostResources(host.CpuCount, cpuUsed, host.ReadMemory(), now), filesystems, stores);
    }

    private async Task<StoreSample> SampleStoreAsync(MaintenanceStoreOptions store, DateTimeOffset now, bool observe, CancellationToken cancellationToken)
    {
        var inventory = await scanner.ScanAsync(store, options.RegisteredImages, now, cancellationToken).ConfigureAwait(false);
        if (observe)
        {
            // 查詢失敗的部分不更新觀測，保留原本的起算時間。
            if (inventory.ContainersAvailable)
            {
                state.Observe(
                    ObservationKeys.ContainerScope(store.Name!),
                    inventory.AllContainers.Where(c => Classifier.IsYmirContainer(store, c, out _, out _))
                        .Select(c => (ObservationKeys.Container(store.Name!, c.Id), Classifier.IsStopped(c.State))),
                    now);
            }

            if (inventory.ContainersAvailable && inventory.ImagesAvailable)
            {
                var references = inventory.AllContainers.Select(c => c.ImageId).ToHashSet(StringComparer.Ordinal);
                state.Observe(
                    ObservationKeys.ImageScope(store.Name!),
                    inventory.AllImages.Where(i => Classifier.IsYmirImage(store, i, options.RegisteredImages))
                        .Select(i => (ObservationKeys.Image(store.Name!, i.Id), !references.Contains(i.Id))),
                    now);
            }
        }

        var context = new ClassificationContext(
            store,
            Policy,
            now,
            inventory.ImagesAvailable ? PinnedImages.Resolve(store, inventory.AllImages, host.ReadFile) : null,
            options.RegisteredImages,
            options.ProtectedImages,
            _excluded,
            options.EffectiveKeepRecentVersions,
            host.DirectoryExists);
        var unused = state.SnapshotUnusedSince();
        var filesystem = inventory.GraphRoot is { } root ? host.ReadFilesystem(root) : null;
        return new StoreSample(store, inventory, filesystem, Classifier.ClassifyContainers(inventory, context, unused), Classifier.ClassifyImages(inventory, context, unused));
    }

    private async Task<FilesystemInfo?> ResolveFilesystemAsync(MaintenanceStoreOptions store, CancellationToken cancellationToken)
    {
        var result = await cli.RunAsync(store, ["info", "--format", "{{.Store.GraphRoot}}"], cancellationToken).ConfigureAwait(false);
        return result.Succeeded && result.Output.Trim() is { Length: > 0 } root ? host.ReadFilesystem(root) : null;
    }

    private static IReadOnlyList<FilesystemResources> ToFilesystemResources(Sample sample) =>
        [.. sample.Filesystems.Select(f => new FilesystemResources(
            f.Id,
            [.. sample.Stores.Where(s => s.Filesystem?.MountPoint == f.MountPoint).Select(s => s.Options.Name!)],
            f.TotalBytes,
            f.AvailableBytes,
            f.AvailablePercent))];

    private static StoreStatus ToStoreStatus(StoreSample store, bool stale)
    {
        var inventory = store.Inventory;
        var status = !inventory.ContainersAvailable || !inventory.ImagesAvailable ? DataStatus.Failed : stale ? DataStatus.Stale : DataStatus.Available;
        return new StoreStatus(inventory.Store, status, inventory.SampledAt, store.Filesystem?.Id, inventory.Error);
    }

    private static ReclaimItem ToItem(Candidate candidate, ReclaimItemResult result, string? reason) =>
        new(candidate.Store, candidate.IsContainer ? "container" : "image", candidate.Name, result, reason);

    private static string Summarize(ReclaimTrigger trigger, int candidates, int deleted, int failed, IReadOnlyList<FilesystemDelta> deltas)
    {
        var reason = trigger switch
        {
            ReclaimTrigger.Retention => "依保留天數",
            ReclaimTrigger.Cpu => "CPU 剩餘不足",
            ReclaimTrigger.Memory => "記憶體可用量不足",
            _ => "磁碟可用量不足",
        };
        if (candidates == 0)
        {
            return trigger is ReclaimTrigger.Cpu or ReclaimTrigger.Memory
                ? $"{reason}：沒有可安全回收的項目（不會停止執行中的工作）。"
                : $"{reason}：沒有可安全回收的項目。";
        }

        var freed = deltas.Sum(d => Math.Max(0, d.AvailableAfterBytes - d.AvailableBeforeBytes));
        return $"{reason}：候選 {candidates}、刪除 {deleted}、失敗 {failed}；檔案系統實測增加可用空間 {freed / 1e9:0.##} GB。";
    }
}
