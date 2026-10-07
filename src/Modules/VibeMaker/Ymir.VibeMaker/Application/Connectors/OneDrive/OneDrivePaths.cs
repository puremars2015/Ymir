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
}
