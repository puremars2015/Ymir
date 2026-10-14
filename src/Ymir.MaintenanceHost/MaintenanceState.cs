using System.Text.Json;
using Ymir.Maintenance;

namespace Ymir.MaintenanceHost;

/// <summary>
/// 持久化的觀測狀態（ADR-0025 §3）：每個物件「開始未使用」的時間、最近同步的政策、上次依保留天數回收的時間。
/// 服務重啟不重設未使用時間；首次遇到的物件以首次觀測時間起算（不以 image 建立時間當作最後使用時間）。
/// </summary>
internal sealed class MaintenanceStateDocument
{
    public Dictionary<string, DateTimeOffset> UnusedSince { get; set; } = new(StringComparer.Ordinal);

    public ReclamationPolicy? Policy { get; set; }

    public DateTimeOffset? LastRetentionSweep { get; set; }
}

/// <summary>狀態檔（state.json）與回收紀錄（runs.jsonl）。寫入先寫暫存檔再改名，避免中斷時留下半個檔案。</summary>
internal sealed class MaintenanceStateStore
{
    private readonly string _statePath;
    private readonly string _runsPath;
    private readonly int _maxRuns;
    private readonly Lock _gate = new();
    private readonly List<ReclaimRun> _runs;

    public MaintenanceStateStore(string directory, int maxRuns)
    {
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        _statePath = Path.Combine(directory, "state.json");
        _runsPath = Path.Combine(directory, "runs.jsonl");
        _maxRuns = maxRuns;
        State = Load<MaintenanceStateDocument>(_statePath) ?? new MaintenanceStateDocument();
        State.UnusedSince = new Dictionary<string, DateTimeOffset>(State.UnusedSince, StringComparer.Ordinal);
        _runs = LoadRuns(_runsPath);
    }

    public MaintenanceStateDocument State { get; }

    /// <summary>
    /// 依最新盤點更新未使用起算時間：未使用的物件第一次出現時記下現在；有使用（container 執行中、image 被引用）時移除，重新未使用再重新起算。
    /// 只更新查詢成功的 store；查詢失敗的 store 保留原本的紀錄，不因暫時失敗而重設。
    /// </summary>
    /// <param name="scope">鍵的前綴（<see cref="ObservationKeys.ContainerScope"/> / <see cref="ObservationKeys.ImageScope"/>）；這個範圍內沒出現的鍵視為已不存在。</param>
    public void Observe(string scope, IEnumerable<(string Key, bool Unused)> objects, DateTimeOffset now)
    {
        lock (_gate)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (key, unused) in objects)
            {
                seen.Add(key);
                if (!unused)
                {
                    State.UnusedSince.Remove(key);
                }
                else
                {
                    State.UnusedSince.TryAdd(key, now);
                }
            }

            // 已經不存在的物件。
            foreach (var key in State.UnusedSince.Keys.Where(k => k.StartsWith(scope, StringComparison.Ordinal) && !seen.Contains(k)).ToList())
            {
                State.UnusedSince.Remove(key);
            }
        }
    }

    public IReadOnlyDictionary<string, DateTimeOffset> SnapshotUnusedSince()
    {
        lock (_gate)
        {
            return new Dictionary<string, DateTimeOffset>(State.UnusedSince, StringComparer.Ordinal);
        }
    }

    public void SetPolicy(ReclamationPolicy policy)
    {
        lock (_gate)
        {
            State.Policy = policy;
        }

        Save();
    }

    public void SetLastRetentionSweep(DateTimeOffset at)
    {
        lock (_gate)
        {
            State.LastRetentionSweep = at;
        }

        Save();
    }

    public void Save()
    {
        string json;
        lock (_gate)
        {
            json = JsonSerializer.Serialize(State, MaintenanceProtocol.JsonOptions);
        }

        WriteAtomically(_statePath, json);
    }

    public ReclaimRun AddRun(Func<long, ReclaimRun> create)
    {
        lock (_gate)
        {
            var run = create((_runs.Count == 0 ? 0 : _runs[^1].Id) + 1);
            _runs.Add(run);
            if (_runs.Count > _maxRuns)
            {
                _runs.RemoveRange(0, _runs.Count - _maxRuns);
            }

            WriteAtomically(_runsPath, string.Join('\n', _runs.Select(r => JsonSerializer.Serialize(r, MaintenanceProtocol.JsonOptions))) + "\n");
            return run;
        }
    }

    /// <summary>id 大於 <paramref name="afterId"/> 的紀錄（由舊到新，最多 <see cref="MaintenanceProtocol.MaxRunsPerPage"/> 筆）；afterId 為 0 時回傳最新的一頁。</summary>
    public IReadOnlyList<ReclaimRun> GetRuns(long afterId)
    {
        lock (_gate)
        {
            return afterId <= 0
                ? [.. _runs.TakeLast(MaintenanceProtocol.MaxRunsPerPage)]
                : [.. _runs.Where(r => r.Id > afterId).Take(MaintenanceProtocol.MaxRunsPerPage)];
        }
    }

    private static void WriteAtomically(string path, string content)
    {
        var temp = path + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var stream = new FileStream(temp, options))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(content);
        }

        File.Move(temp, path, overwrite: true);
    }

    private static T? Load<T>(string path)
        where T : class
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), MaintenanceProtocol.JsonOptions) : null;
        }
        catch (JsonException)
        {
            // 損毀的狀態檔：重新開始觀測（未使用時間從現在起算，只會更晚回收，不會更早）。
            return null;
        }
    }

    private static List<ReclaimRun> LoadRuns(string path)
    {
        var runs = new List<ReclaimRun>();
        if (!File.Exists(path))
        {
            return runs;
        }

        foreach (var line in File.ReadLines(path).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            try
            {
                if (JsonSerializer.Deserialize<ReclaimRun>(line, MaintenanceProtocol.JsonOptions) is { } run)
                {
                    runs.Add(run);
                }
            }
            catch (JsonException)
            {
                // 略過損毀的一行。
            }
        }

        return runs;
    }
}
