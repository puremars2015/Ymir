namespace Ymir.MaintenanceHost;

/// <summary>
/// 正式設定指向的 image（ADR-0025 §4）：API Quadlet 的 <c>Image=</c>、runtime host 的 <c>VibeMaker__Runtime__Image</c>。
/// 任一設定檔讀不到、沒有該 key，或值對應不到 store 內的 image 時回傳 null，整個 store 的 image 一律保護（fail closed）。
/// </summary>
internal static class PinnedImages
{
    public static IReadOnlySet<string>? Resolve(
        MaintenanceStoreOptions store,
        IReadOnlyList<ImageFact> images,
        Func<string, string?> readFile)
    {
        var pinned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in store.PinnedImageSources)
        {
            var content = readFile(source.File!);
            if (content is null || ReadValue(content, source.Key!) is not { } reference || Find(images, reference) is not { } id)
            {
                return null;
            }

            pinned.Add(id);
        }

        return pinned;
    }

    /// <summary>讀取 <c>Key=value</c>（忽略註解行，去掉引號）；同一個 key 出現多次時取最後一個（與 systemd 相同）。</summary>
    public static string? ReadValue(string content, string key)
    {
        string? value = null;
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is '#' or ';' || !line.StartsWith(key + "=", StringComparison.Ordinal))
            {
                continue;
            }

            value = line[(key.Length + 1)..].Trim().Trim('"', '\'');
        }

        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>值可以是 image ID（12～64 碼，可帶 sha256:）或 name[:tag]（沒有 tag 視為 latest）。</summary>
    public static string? Find(IReadOnlyList<ImageFact> images, string reference)
    {
        var id = PodmanJson.NormalizeId(reference);
        if (id.Length is >= 12 and <= 64 && id.All(Uri.IsHexDigit))
        {
            var byId = images.Where(i => i.Id.StartsWith(id, StringComparison.Ordinal)).ToList();
            return byId.Count == 1 ? byId[0].Id : null;
        }

        var tag = Classifier.RepositoryOf(reference) == reference ? reference + ":latest" : reference;
        return images.FirstOrDefault(i => i.Tags.Contains(tag, StringComparer.Ordinal))?.Id;
    }
}
