namespace Ymir.VibeMaker.Application.Connectors.OneDrive;

/// <summary>OneDrive 資料夾路徑與名稱的規則（ADR-0013 §3）。只接受使用者輸入的名稱，由後端在使用者自己的 drive 解析。</summary>
public static class OneDrivePaths
{
    public const string DefaultRoot = "/Ymir";
    public const int MaxSegmentLength = 100;
    public const int MaxDepth = 5;

    private const string InvalidChars = "\"*:<>?\\|";

    private static readonly System.Buffers.SearchValues<char> s_invalidChars = System.Buffers.SearchValues.Create(InvalidChars);

    /// <summary>整理成 <c>/A/B</c>；不合法時回傳 null 與給使用者看的原因。</summary>
    public static (string? Path, string? Problem) NormalizeRoot(string? input)
    {
        var segments = (input ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return (null, "請輸入資料夾名稱，例如 /Ymir。");
        }

        if (segments.Length > MaxDepth)
        {
            return (null, $"資料夾最多 {MaxDepth} 層。");
        }

        foreach (var segment in segments)
        {
            if (!IsValidName(segment))
            {
                return (null, $"資料夾名稱不能包含 {string.Join(' ', InvalidChars.ToCharArray())}、控制字元，不能是 . 或 ..，也不能以 . 結尾，長度最多 {MaxSegmentLength} 字。");
            }
        }

        return ("/" + string.Join('/', segments), null);
    }

    public static bool IsValidName(string name) =>
        name.Length is > 0 and <= MaxSegmentLength
        && name is not ("." or "..")
        && !name.EndsWith('.')
        && name.IndexOfAny(s_invalidChars) < 0
        && !name.Any(char.IsControl);

    public static IReadOnlyList<string> Segments(string normalizedPath) =>
        normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// 專案 / 對話在 OneDrive 的資料夾名稱：<c>名稱-id 前 8 碼</c>（ADR-0013 §3）。名稱中 OneDrive 不接受的字元換成 <c>_</c>，
    /// 過長截斷；id 讓同名的專案不會共用資料夾。只在第一次建立時使用，之後以 item id 對應，改名不影響。
    /// </summary>
    public static string FolderName(string? displayName, Guid id)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var c in (displayName ?? string.Empty).Trim())
        {
            builder.Append(char.IsControl(c) || c == '/' || InvalidChars.Contains(c, StringComparison.Ordinal) ? '_' : c);
        }

        var name = builder.ToString().Trim().TrimEnd('.');
        if (name.Length > 60)
        {
            name = name[..60].TrimEnd().TrimEnd('.');
        }

        return $"{(name.Length == 0 ? "未命名" : name)}-{id.ToString("N")[..8]}";
    }

    /// <summary>衝突副本的路徑：<c>dir/名稱 (OneDrive 衝突 yyyyMMdd-HHmmss).副檔名</c>（ADR-0013 §4，兩邊都保留）。</summary>
    public static string ConflictPath(string relativePath, DateTimeOffset now)
    {
        var slash = relativePath.LastIndexOf('/');
        var directory = slash < 0 ? string.Empty : relativePath[..(slash + 1)];
        var fileName = relativePath[(slash + 1)..];
        var dot = fileName.LastIndexOf('.');
        var (stem, extension) = dot > 0 ? (fileName[..dot], fileName[dot..]) : (fileName, string.Empty);
        return $"{directory}{stem} (OneDrive 衝突 {now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)}){extension}";
    }

    /// <summary>工作目錄內的相對路徑能否同步到 OneDrive：每一段都必須是 OneDrive 接受的名稱，且不是隱藏檔或 node_modules。</summary>
    public static bool IsSyncablePath(string relativePath) =>
        Files.WorkspacePathRules.IsSafeRelativePath(relativePath)
        && relativePath.Split('/').All(segment => IsValidName(segment) && !segment.EndsWith(' '));
}
