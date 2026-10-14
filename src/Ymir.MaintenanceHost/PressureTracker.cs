using Ymir.Maintenance;

namespace Ymir.MaintenanceHost;

/// <summary>一個門檻在某次採樣的量測：<see cref="Below"/> 低於門檻、<see cref="Recovered"/> 已回到回復門檻（門檻加上遲滯）。</summary>
internal sealed record PressureReading(string Key, ReclaimTrigger Trigger, bool Below, bool Recovered);

/// <summary>
/// 門檻遲滯（ADR-0025 §3）：低於門檻持續 <c>Sustain</c> 才觸發；觸發後直到回到回復門檻才解除；同一個門檻兩輪之間至少間隔 <c>Cooldown</c>。
/// 短暫的負載尖峰不會觸發，也不會因為資源一直不足而每次採樣都清理（避免重複清理風暴）。
/// </summary>
internal sealed class PressureTracker(TimeSpan sustain, TimeSpan cooldown)
{
    private sealed class State
    {
        public DateTimeOffset? BelowSince { get; set; }

        public bool Triggered { get; set; }

        public DateTimeOffset? LastRoundAt { get; set; }

        public bool Below { get; set; }
    }

    private readonly Dictionary<string, State> _states = new(StringComparer.Ordinal);

    /// <summary>套用一次採樣，回傳這次應該執行緊急回收的門檻。</summary>
    public IReadOnlyList<PressureReading> Update(IReadOnlyList<PressureReading> readings, DateTimeOffset now)
    {
        var fire = new List<PressureReading>();
        foreach (var reading in readings)
        {
            var state = _states.TryGetValue(reading.Key, out var existing) ? existing : _states[reading.Key] = new State();
            state.Below = reading.Below;
            if (state.Triggered && reading.Recovered)
            {
                state.Triggered = false;
                state.BelowSince = null;
            }

            if (reading.Below)
            {
                state.BelowSince ??= now;
            }
            else if (!state.Triggered)
            {
                state.BelowSince = null;
            }

            if (reading.Below
                && now - state.BelowSince >= sustain
                && (state.LastRoundAt is null || now - state.LastRoundAt >= cooldown))
            {
                state.Triggered = true;
                state.LastRoundAt = now;
                fire.Add(reading);
            }
        }

        // 門檻停用或檔案系統消失：移除狀態。
        foreach (var key in _states.Keys.Where(k => readings.All(r => r.Key != k)).ToList())
        {
            _states.Remove(key);
        }

        return fire;
    }

    public IReadOnlyList<PressureStatus> Snapshot() =>
        [.. _states.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => new PressureStatus(s.Key, s.Value.Below, s.Value.BelowSince, s.Value.Triggered))];

    /// <summary>依政策產生這次採樣的量測；門檻為 0 的項目不產生量測。</summary>
    public static IReadOnlyList<PressureReading> Evaluate(
        ReclamationPolicy policy,
        double recoveryMarginPercent,
        HostResources host,
        IReadOnlyList<FilesystemResources> filesystems)
    {
        var readings = new List<PressureReading>();
        if (policy.CpuMinFreePercent > 0 && host.CpuFreePercent is { } cpuFree)
        {
            readings.Add(new PressureReading("cpu", ReclaimTrigger.Cpu, cpuFree < policy.CpuMinFreePercent, cpuFree >= policy.CpuMinFreePercent + recoveryMarginPercent));
        }

        if ((policy.MemoryMinAvailableGb > 0 || policy.MemoryMinAvailablePercent > 0) && host.Status == DataStatus.Available)
        {
            var (below, recovered) = Capacity(host.MemoryAvailableBytes, host.MemoryAvailablePercent, policy.MemoryMinAvailableGb, policy.MemoryMinAvailablePercent, recoveryMarginPercent);
            readings.Add(new PressureReading("memory", ReclaimTrigger.Memory, below, recovered));
        }

        if (policy.DiskMinAvailableGb > 0 || policy.DiskMinAvailablePercent > 0)
        {
            foreach (var fs in filesystems)
            {
                var (below, recovered) = Capacity(fs.AvailableBytes, fs.AvailablePercent, policy.DiskMinAvailableGb, policy.DiskMinAvailablePercent, recoveryMarginPercent);
                readings.Add(new PressureReading(DiskKey(fs.Id), ReclaimTrigger.Disk, below, recovered));
            }
        }

        return readings;
    }

    public static string DiskKey(string filesystemId) => "disk:" + filesystemId;

    /// <summary>容量門檻：低於 GB 或比例任一門檻即不足；回復需要兩者都高於「門檻加上遲滯」。</summary>
    public static (bool Below, bool Recovered) Capacity(long availableBytes, double availablePercent, double minGb, double minPercent, double marginPercent)
    {
        var availableGb = availableBytes / 1e9;
        var below = (minGb > 0 && availableGb < minGb) || (minPercent > 0 && availablePercent < minPercent);
        var recovered = (minGb <= 0 || availableGb >= minGb * (1 + (marginPercent / 100)))
            && (minPercent <= 0 || availablePercent >= minPercent + marginPercent);
        return (below, recovered);
    }
}
