using System.Text.Json;
using System.Text.Json.Nodes;
using Ymir.MaintenanceHost;

namespace Ymir.UnitTests.Maintenance;

internal sealed class FakeContainer
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string ImageId { get; init; }

    public string State { get; set; } = "exited";

    public Dictionary<string, string> Labels { get; init; } = [];

    public List<(string Type, string Source, string Destination)> Mounts { get; init; } = [];
}

internal sealed class FakeImage
{
    public required string Id { get; init; }

    public List<string> Tags { get; init; } = [];

    public long Size { get; init; } = 100_000_000;

    public DateTimeOffset Created { get; init; } = DateTimeOffset.UnixEpoch;

    public Dictionary<string, string> Labels { get; init; } = [];

    public List<string> Layers { get; init; } = [];
}

internal sealed class FakeStore
{
    public string GraphRoot { get; set; } = "/var/lib/containers/storage";

    public List<FakeContainer> Containers { get; } = [];

    public List<FakeImage> Images { get; } = [];

    public bool FailPs { get; set; }

    public bool FailImages { get; set; }

    public bool FailInspect { get; set; }
}

/// <summary>以記憶體中的 container / image 回應 StoreScanner 使用的 podman 指令；記錄所有呼叫以驗證不會使用 --force。</summary>
internal sealed class FakeContainerCli : IContainerCli
{
    public Dictionary<string, FakeStore> Stores { get; } = new(StringComparer.Ordinal);

    public List<(string Store, IReadOnlyList<string> Arguments)> Calls { get; } = [];

    /// <summary>在刪除前的狀態確認時改變狀態（模擬競態）。</summary>
    public Action<FakeStore>? BeforeStateCheck { get; set; }

    /// <summary>刪除後減少的可用空間（模擬實際釋放）。</summary>
    public Action<string, long>? OnFreed { get; set; }

    public Task<CliResult> RunAsync(MaintenanceStoreOptions store, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        Calls.Add((store.Name!, arguments));
        var s = Stores[store.Name!];
        return Task.FromResult(arguments[0] switch
        {
            "info" => Ok(s.GraphRoot),
            "ps" => s.FailPs ? Fail() : Ok(new JsonArray([.. s.Containers.Select(ContainerJson)]).ToJsonString()),
            "images" => s.FailImages ? Fail() : Ok(new JsonArray([.. s.Images.Select(ImageJson)]).ToJsonString()),
            "container" when arguments.Contains("{{.State.Status}}") => StateOf(s, arguments[^1]),
            "container" => s.FailInspect ? Fail() : Ok(new JsonArray([.. s.Containers.Where(c => arguments.Contains(c.Id)).Select(InspectJson)]).ToJsonString()),
            "stats" => Ok("[]"),
            "image" => Ok(new JsonArray([.. s.Images.Select(i => (JsonNode)new JsonObject { ["Id"] = i.Id, ["RootFS"] = new JsonObject { ["Layers"] = new JsonArray([.. i.Layers.Select(l => (JsonNode)l)]) } })]).ToJsonString()),
            "rm" => Remove(store.Name!, s, arguments),
            "rmi" => RemoveImage(store.Name!, s, arguments),
            _ => Fail(),
        });
    }

    private CliResult StateOf(FakeStore store, string id)
    {
        BeforeStateCheck?.Invoke(store);
        return store.Containers.FirstOrDefault(c => c.Id == id) is { } c ? Ok(c.State) : Fail();
    }

    private CliResult Remove(string storeName, FakeStore store, IReadOnlyList<string> arguments)
    {
        var target = store.Containers.FirstOrDefault(c => c.Id == arguments[^1]);
        if (target is null || target.State == "running")
        {
            return Fail();
        }

        store.Containers.Remove(target);
        OnFreed?.Invoke(storeName, 0);
        return Ok(string.Empty);
    }

    private CliResult RemoveImage(string storeName, FakeStore store, IReadOnlyList<string> arguments)
    {
        foreach (var target in arguments.Skip(1))
        {
            var image = store.Images.FirstOrDefault(i => i.Id == target || i.Tags.Contains(target));
            if (image is null || store.Containers.Any(c => c.ImageId == image.Id))
            {
                return Fail();
            }

            image.Tags.Remove(target);
            if (image.Tags.Count == 0)
            {
                store.Images.Remove(image);
                OnFreed?.Invoke(storeName, image.Size);
            }
        }

        return Ok(string.Empty);
    }

    private static JsonNode ContainerJson(FakeContainer c) => new JsonObject
    {
        ["Id"] = c.Id,
        ["Names"] = new JsonArray(c.Name),
        ["ImageID"] = c.ImageId,
        ["Image"] = "localhost/ymir/agent-runtime:dev",
        ["State"] = c.State,
        ["Labels"] = JsonSerializer.SerializeToNode(c.Labels),
    };

    private static JsonNode InspectJson(FakeContainer c) => new JsonObject
    {
        ["Id"] = c.Id,
        ["State"] = new JsonObject { ["Status"] = c.State, ["Health"] = new JsonObject { ["Status"] = string.Empty } },
        ["Mounts"] = new JsonArray([.. c.Mounts.Select(m => (JsonNode)new JsonObject { ["Type"] = m.Type, ["Source"] = m.Source, ["Destination"] = m.Destination })]),
    };

    private static JsonNode ImageJson(FakeImage i) => new JsonObject
    {
        ["Id"] = i.Id,
        ["Names"] = i.Tags.Count == 0 ? null : new JsonArray([.. i.Tags.Select(t => (JsonNode)t)]),
        ["Size"] = i.Size,
        ["Created"] = i.Created.ToUnixTimeSeconds(),
        ["Labels"] = JsonSerializer.SerializeToNode(i.Labels),
    };

    private static CliResult Ok(string output) => new(0, output, string.Empty);

    private static CliResult Fail() => new(125, string.Empty, "error");
}

internal sealed class FakeHostMetrics : IHostMetricsSource
{
    public int CpuCount => 4;

    public CpuTimes? Cpu { get; set; } = new(0, 0);

    public MemoryInfo? Memory { get; set; } = new(16_000_000_000, 8_000_000_000);

    /// <summary>graph root → 檔案系統。</summary>
    public Dictionary<string, FilesystemInfo> Filesystems { get; } = new(StringComparer.Ordinal);

    public HashSet<string> Directories { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

    public CpuTimes? ReadCpuTimes() => Cpu;

    public MemoryInfo? ReadMemory() => Memory;

    public FilesystemInfo? ReadFilesystem(string path) => Filesystems.TryGetValue(path, out var fs) ? fs : null;

    public bool DirectoryExists(string path) => Directories.Contains(path) || Directory.Exists(path);

    public string? ReadFile(string path) => Files.TryGetValue(path, out var content) ? content : null;
}
