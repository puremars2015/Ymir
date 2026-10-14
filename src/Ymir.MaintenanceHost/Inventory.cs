using System.Globalization;
using System.Text.Json;

namespace Ymir.MaintenanceHost;

internal sealed record MountFact(string Type, string Source, string Destination);

/// <param name="State">podman 的狀態（running、exited、paused、created…）。</param>
internal sealed record ContainerFact(
    string Id,
    string Name,
    string ImageId,
    string ImageName,
    string State,
    IReadOnlyDictionary<string, string> Labels)
{
    public string? Health { get; init; }

    public IReadOnlyList<MountFact>? Mounts { get; init; }

    public double? CpuPercent { get; init; }

    public long? MemoryBytes { get; init; }
}

internal sealed record ImageFact(
    string Id,
    IReadOnlyList<string> Tags,
    long SizeBytes,
    DateTimeOffset Created,
    IReadOnlyDictionary<string, string> Labels)
{
    public IReadOnlyList<string> Layers { get; init; } = [];

    public bool IsDangling => Tags.Count == 0;
}

/// <summary>
/// 一個 store 的盤點結果。<see cref="ContainersAvailable"/> 為 false 時（<c>ps</c> 失敗）不可視為沒有引用：所有 image 一律 fail closed。
/// </summary>
internal sealed record StoreInventory(
    string Store,
    DateTimeOffset SampledAt,
    bool ContainersAvailable,
    IReadOnlyList<ContainerFact> AllContainers,
    bool ImagesAvailable,
    IReadOnlyList<ImageFact> AllImages,
    string? GraphRoot,
    string? Error);

/// <summary>解析 podman 的 JSON 輸出（Podman 4.4+ / 5.x 的欄位名稱）。只取需要的欄位，格式不符時拋出 <see cref="JsonException"/>。</summary>
internal static class PodmanJson
{
    public static IReadOnlyList<ContainerFact> ParseContainers(string json)
    {
        using var document = JsonDocument.Parse(EmptyArrayIfBlank(json));
        var result = new List<ContainerFact>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var names = item.TryGetProperty("Names", out var n) && n.ValueKind == JsonValueKind.Array ? n.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList() : [];
            result.Add(new ContainerFact(
                NormalizeId(RequiredString(item, "Id")),
                names.FirstOrDefault() ?? string.Empty,
                NormalizeId(OptionalString(item, "ImageID") ?? string.Empty),
                OptionalString(item, "Image") ?? string.Empty,
                (OptionalString(item, "State") ?? "unknown").ToLowerInvariant(),
                ParseLabels(item)));
        }

        return result;
    }

    /// <summary><c>podman container inspect --format json</c>：健康狀態與掛載（Source / Destination）。</summary>
    public static IReadOnlyDictionary<string, (string? Health, IReadOnlyList<MountFact> Mounts, string State)> ParseContainerInspect(string json)
    {
        using var document = JsonDocument.Parse(EmptyArrayIfBlank(json));
        var result = new Dictionary<string, (string?, IReadOnlyList<MountFact>, string)>(StringComparer.Ordinal);
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var id = NormalizeId(RequiredString(item, "Id"));
            string? health = null;
            var state = "unknown";
            if (item.TryGetProperty("State", out var s) && s.ValueKind == JsonValueKind.Object)
            {
                state = (OptionalString(s, "Status") ?? "unknown").ToLowerInvariant();
                if (s.TryGetProperty("Health", out var h) && h.ValueKind == JsonValueKind.Object)
                {
                    health = OptionalString(h, "Status") is { Length: > 0 } status ? status : null;
                }
            }

            var mounts = new List<MountFact>();
            if (item.TryGetProperty("Mounts", out var m) && m.ValueKind == JsonValueKind.Array)
            {
                foreach (var mount in m.EnumerateArray())
                {
                    mounts.Add(new MountFact(OptionalString(mount, "Type") ?? string.Empty, OptionalString(mount, "Source") ?? string.Empty, OptionalString(mount, "Destination") ?? string.Empty));
                }
            }

            result[id] = (health, mounts, state);
        }

        return result;
    }

    /// <summary><c>podman stats --no-stream --format json</c>：id 為 12 碼短 ID，CPU 與記憶體是帶單位的字串。</summary>
    public static IReadOnlyDictionary<string, (double? Cpu, long? Memory)> ParseStats(string json)
    {
        using var document = JsonDocument.Parse(EmptyArrayIfBlank(json));
        var result = new Dictionary<string, (double?, long?)>(StringComparer.Ordinal);
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var id = OptionalString(item, "id") ?? OptionalString(item, "ID");
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var cpu = ParsePercent(OptionalString(item, "cpu_percent") ?? OptionalString(item, "CPU"));
            var memoryText = OptionalString(item, "mem_usage") ?? OptionalString(item, "MemUsage");
            var memory = memoryText is null ? null : ParseSize(memoryText.Split('/')[0]);
            result[NormalizeId(id)[..Math.Min(12, NormalizeId(id).Length)]] = (cpu, memory);
        }

        return result;
    }

    public static IReadOnlyList<ImageFact> ParseImages(string json)
    {
        using var document = JsonDocument.Parse(EmptyArrayIfBlank(json));
        var result = new List<ImageFact>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            // Names（Podman 5）或 RepoTags；dangling 的值是 null 或包含 <none>:<none>。
            var tags = ReadStrings(item, "Names").Concat(ReadStrings(item, "RepoTags"))
                .Where(t => !string.IsNullOrEmpty(t) && !t.StartsWith("<none>", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var created = item.TryGetProperty("Created", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : DateTimeOffset.MinValue;
            var size = item.TryGetProperty("Size", out var sz) && sz.ValueKind == JsonValueKind.Number && sz.TryGetInt64(out var bytes) ? bytes : 0;
            result.Add(new ImageFact(NormalizeId(RequiredString(item, "Id")), tags, size, created, ParseLabels(item)));
        }

        // podman images 會把有多個 tag 的 image 列成多筆（同一個 Id），合併成一筆。
        return [.. result.GroupBy(i => i.Id, StringComparer.Ordinal).Select(g => g.First() with { Tags = [.. g.SelectMany(i => i.Tags).Distinct(StringComparer.Ordinal)] })];
    }

    /// <summary><c>podman image inspect --format json</c>：每個 image 的 RootFS layer，用來判斷共用 layer。</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseImageLayers(string json)
    {
        using var document = JsonDocument.Parse(EmptyArrayIfBlank(json));
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var layers = item.TryGetProperty("RootFS", out var root) && root.ValueKind == JsonValueKind.Object
                ? ReadStrings(root, "Layers").ToList()
                : [];
            result[NormalizeId(RequiredString(item, "Id"))] = layers;
        }

        return result;
    }

    public static string NormalizeId(string id) => id.StartsWith("sha256:", StringComparison.Ordinal) ? id["sha256:".Length..] : id;

    /// <summary>「4.59%」→ 4.59。</summary>
    public static double? ParsePercent(string? value) =>
        value is not null && double.TryParse(value.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;

    /// <summary>「278.7MB」「16.23GB」「512kB」「0B」（podman 使用十進位單位）→ bytes。</summary>
    public static long? ParseSize(string value)
    {
        var text = value.Trim();
        var unitStart = text.Length;
        while (unitStart > 0 && char.IsLetter(text[unitStart - 1]))
        {
            unitStart--;
        }

        if (!double.TryParse(text[..unitStart], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }

        double? multiplier = text[unitStart..].ToUpperInvariant() switch
        {
            "" or "B" => 1,
            "KB" => 1e3,
            "MB" => 1e6,
            "GB" => 1e9,
            "TB" => 1e12,
            "KIB" => 1024,
            "MIB" => 1024d * 1024,
            "GIB" => 1024d * 1024 * 1024,
            _ => null,
        };
        return multiplier is { } m ? (long)(number * m) : null;
    }

    private static string EmptyArrayIfBlank(string json) => string.IsNullOrWhiteSpace(json) || json.Trim() == "null" ? "[]" : json;

    private static Dictionary<string, string> ParseLabels(JsonElement item)
    {
        if (!item.TryGetProperty("Labels", out var labels) || labels.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>();
        }

        return labels.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? string.Empty : p.Value.ToString(), StringComparer.Ordinal);
    }

    private static IEnumerable<string> ReadStrings(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!)
            : [];

    private static string RequiredString(JsonElement item, string name) =>
        OptionalString(item, name) is { Length: > 0 } value ? value : throw new JsonException($"Missing {name}.");

    private static string? OptionalString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
