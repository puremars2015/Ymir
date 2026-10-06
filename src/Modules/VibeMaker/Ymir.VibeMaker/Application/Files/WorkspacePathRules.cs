namespace Ymir.VibeMaker.Application.Files;

/// <summary>
/// 下載路徑的檢查（SA §12、CLAUDE.md 安全紅線）：只接受工作目錄內的相對路徑。
/// 這是第一道防線；runtime 內讀取時還會解析 realpath，確認沒有經由 symlink 跑到工作目錄外。
/// </summary>
public static class WorkspacePathRules
{
    public const int MaxPathLength = 1024;

    public static bool IsSafeRelativePath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > MaxPathLength || path[0] == '/' || path.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        if (path.Any(char.IsControl))
        {
            return false;
        }

        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment == "." || segment == ".." || IsHidden(segment))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>隱藏檔（<c>.git</c>、<c>.env</c>…）與 <c>node_modules</c> 不列出也不提供下載。</summary>
    public static bool IsHidden(string segment) =>
        segment.StartsWith('.') || segment.Equals("node_modules", StringComparison.Ordinal);

    /// <summary>下載時的檔名：路徑最後一段。</summary>
    public static string FileName(string path) => path[(path.LastIndexOf('/') + 1)..];
}
