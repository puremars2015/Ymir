using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ymir.Maintenance;

namespace Ymir.MaintenanceHost;

internal sealed record CpuTimes(ulong Idle, ulong Total);

internal sealed record MemoryInfo(long TotalBytes, long AvailableBytes);

/// <summary>檔案系統的容量（內部用：含掛載點，只用來比對 store，不回傳給 API）。</summary>
internal sealed record FilesystemInfo(string MountPoint, long TotalBytes, long AvailableBytes)
{
    /// <summary>不回傳掛載路徑（ADR-0025 §1）：以掛載點的雜湊產生穩定的代號。</summary>
    public string Id => "fs-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(MountPoint)))[..8];

    public double AvailablePercent => TotalBytes <= 0 ? 0 : Math.Round(100.0 * AvailableBytes / TotalBytes, 1);
}

/// <summary>主機資源來源（測試以假的實作取代）。讀的是整台主機（/proc），不是任何 container 的 cgroup。</summary>
internal interface IHostMetricsSource
{
    int CpuCount { get; }

    CpuTimes? ReadCpuTimes();

    MemoryInfo? ReadMemory();

    /// <summary>包含 <paramref name="path"/> 的檔案系統；找不到時為 null。</summary>
    FilesystemInfo? ReadFilesystem(string path);

    bool DirectoryExists(string path);

    /// <summary>讀取設定檔（正式設定指向的 image）；無法讀取時為 null。</summary>
    string? ReadFile(string path);
}

internal sealed class ProcHostMetricsSource : IHostMetricsSource
{
    public int CpuCount => Environment.ProcessorCount;

    public CpuTimes? ReadCpuTimes()
    {
        try
        {
            var line = File.ReadLines("/proc/stat").FirstOrDefault(l => l.StartsWith("cpu ", StringComparison.Ordinal));
            return line is null ? null : HostMetrics.ParseCpuLine(line);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public MemoryInfo? ReadMemory()
    {
        try
        {
            return HostMetrics.ParseMeminfo(File.ReadAllText("/proc/meminfo"));
        }
        catch (IOException)
        {
            return null;
        }
    }

    public FilesystemInfo? ReadFilesystem(string path)
    {
        var drive = DriveInfo.GetDrives()
            .Where(d => d.IsReady && IsUnder(path, d.Name))
            .OrderByDescending(d => d.Name.Length)
            .FirstOrDefault();
        return drive is null ? null : new FilesystemInfo(drive.Name, drive.TotalSize, drive.AvailableFreeSpace);
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public string? ReadFile(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsUnder(string path, string mountPoint)
    {
        var mount = mountPoint.TrimEnd('/') + "/";
        return mount == "/" || (path.TrimEnd('/') + "/").StartsWith(mount, StringComparison.Ordinal);
    }
}

internal static class HostMetrics
{
    /// <summary><c>cpu  user nice system idle iowait irq softirq steal guest guest_nice</c>；idle 含 iowait，total 不含 guest（已算在 user）。</summary>
    public static CpuTimes? ParseCpuLine(string line)
    {
        var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(8).ToList();
        if (fields.Count < 4)
        {
            return null;
        }

        var values = new List<ulong>();
        foreach (var field in fields)
        {
            if (!ulong.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                return null;
            }

            values.Add(value);
        }

        var idle = values[3] + (values.Count > 4 ? values[4] : 0);
        return new CpuTimes(idle, values.Aggregate(0UL, (sum, v) => sum + v));
    }

    public static MemoryInfo? ParseMeminfo(string text)
    {
        long? total = null;
        long? available = null;
        foreach (var line in text.Split('\n'))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var kb))
            {
                continue;
            }

            if (parts[0] == "MemTotal:")
            {
                total = kb * 1024;
            }
            else if (parts[0] == "MemAvailable:")
            {
                available = kb * 1024;
            }
        }

        return total is { } t && available is { } a ? new MemoryInfo(t, a) : null;
    }

    /// <summary>兩次採樣之間的 CPU 使用率（0～100）；計數器沒有前進時為 null。</summary>
    public static double? CpuUsedPercent(CpuTimes? previous, CpuTimes? current)
    {
        if (previous is null || current is null || current.Total <= previous.Total || current.Idle < previous.Idle)
        {
            return null;
        }

        var total = (double)(current.Total - previous.Total);
        var idle = (double)(current.Idle - previous.Idle);
        return Math.Round(Math.Clamp(100.0 * (1 - (idle / total)), 0, 100), 1);
    }

    public static HostResources ToHostResources(int cpuCount, double? cpuUsed, MemoryInfo? memory, DateTimeOffset sampledAt) =>
        memory is null
            ? new HostResources(DataStatus.Failed, sampledAt, cpuCount, cpuUsed, cpuUsed is { } u ? Math.Round(100 - u, 1) : null, 0, 0, 0)
            : new HostResources(
                DataStatus.Available,
                sampledAt,
                cpuCount,
                cpuUsed,
                cpuUsed is { } used ? Math.Round(100 - used, 1) : null,
                memory.TotalBytes,
                memory.AvailableBytes,
                memory.TotalBytes <= 0 ? 0 : Math.Round(100.0 * memory.AvailableBytes / memory.TotalBytes, 1));
}
