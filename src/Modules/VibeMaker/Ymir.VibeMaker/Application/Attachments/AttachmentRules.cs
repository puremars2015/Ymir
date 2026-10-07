using System.Globalization;
using System.Text;
using Ymir.VibeMaker.Application.Files;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Attachments;

/// <summary>
/// 訊息附件的規則：上限、檔名清理、類型判斷、給 Agent 的說明文字。
/// 檔案存放路徑一律由伺服器產生（<c>uploads/{id 末 8 碼}-{清理後的檔名}</c>），不接受外部傳入的路徑（CLAUDE.md 安全紅線）。
/// </summary>
public static class AttachmentRules
{
    /// <summary>工作目錄內放上傳檔案的子目錄（不是隱藏目錄，所以會出現在檔案面板，也能下載）。</summary>
    public const string UploadDirectory = "uploads";

    /// <summary>單一檔案上限。Cloudflare Tunnel（ADR-0006）的單一請求上限是 100 MB，這裡保守設定。</summary>
    public const long MaxFileBytes = 50L * 1024 * 1024;

    public const int MaxAttachmentsPerMessage = 10;

    /// <summary>直接附給支援視覺的模型的圖片：單張與張數上限（其餘仍在工作目錄，Agent 可自行用工具讀取）。</summary>
    public const long MaxInlineImageBytes = 10L * 1024 * 1024;

    public const int MaxInlineImages = 5;

    private const int MaxStoredNameLength = 120;

    /// <summary>可以直接附給模型的圖片格式（OpenAI 相容 API 普遍支援的格式）。</summary>
    private static readonly HashSet<string> s_inlineImageTypes = new(StringComparer.Ordinal) { "image/png", "image/jpeg", "image/gif", "image/webp" };

    /// <summary>非圖片只依副檔名給一個顯示用的類型；不影響下載（下載一律 octet-stream 附件）。</summary>
    private static readonly Dictionary<string, string> s_extensionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = "text/plain",
        [".md"] = "text/markdown",
        [".csv"] = "text/csv",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".svg"] = "image/svg+xml",
        [".pdf"] = "application/pdf",
        [".zip"] = "application/zip",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".mp4"] = "video/mp4",
        [".m4v"] = "video/mp4",
        [".mov"] = "video/quicktime",
        [".webm"] = "video/webm",
        [".avi"] = "video/x-msvideo",
        [".mkv"] = "video/x-matroska",
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
        [".m4a"] = "audio/mp4",
    };

    /// <summary>
    /// 清理使用者提供的檔名：只取最後一段（去掉任何目錄）、移除控制字元與檔名不允許的字元、去掉開頭的 <c>.</c>（隱藏檔不會被列出），
    /// 過長時保留副檔名截斷。清理後為空回傳 null。
    /// </summary>
    public static string? SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var name = fileName.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        var builder = new StringBuilder(name.Length);
        foreach (var c in name.Normalize(NormalizationForm.FormC))
        {
            builder.Append(char.IsControl(c) || c is ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '_' : c);
        }

        name = builder.ToString().Trim().TrimStart('.').Trim();
        if (name.Length == 0 || name.Equals("node_modules", StringComparison.Ordinal))
        {
            return null;
        }

        if (name.Length > MaxStoredNameLength)
        {
            var extension = System.IO.Path.GetExtension(name);
            extension = extension.Length is > 0 and <= 16 ? extension : string.Empty;
            name = name[..(MaxStoredNameLength - extension.Length)].TrimEnd() + extension;
        }

        return name;
    }

    /// <summary>
    /// 附件在工作目錄內的路徑：以附件 id 區分同名檔案。取 id 的<b>末</b> 8 碼：Guid v7 的開頭是時間戳，
    /// 同一分鐘內上傳的附件前 8 碼相同，同名檔案會互相覆蓋；末段是隨機值。
    /// </summary>
    public static string StoragePath(Guid attachmentId, string sanitizedFileName)
    {
        var path = $"{UploadDirectory}/{attachmentId.ToString("N", CultureInfo.InvariantCulture)[^8..]}-{sanitizedFileName}";
        return WorkspacePathRules.IsSafeRelativePath(path) && path.Length <= MessageAttachment.PathMaxLength
            ? path
            : throw new InvalidOperationException("Generated attachment path is not safe.");
    }

    /// <summary>
    /// 判斷類型：圖片以檔頭（magic bytes）判斷，不信任瀏覽器的 Content-Type 與副檔名；其他類型依副檔名，認不得時為 octet-stream。
    /// </summary>
    public static string DetectContentType(ReadOnlySpan<byte> header, string fileName)
    {
        if (header.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        if (header.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }

        if (header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8))
        {
            return "image/gif";
        }

        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        var byExtension = s_extensionTypes.TryGetValue(System.IO.Path.GetExtension(fileName), out var type) ? type : "application/octet-stream";
        // 副檔名是圖片、檔頭卻不是：不當成圖片（避免把任意內容當圖片送給模型）。SVG 是文字格式，維持依副檔名。
        return byExtension;
    }

    public static bool IsInlineImage(string contentType) => s_inlineImageTypes.Contains(contentType);

    /// <summary>
    /// 附加在送給 Agent 的內容後面的說明：列出檔案路徑，讓 Agent 用工具讀取或處理（影片、文件等模型無法直接看的格式也適用）。
    /// 對話紀錄仍只保存使用者輸入的文字。
    /// </summary>
    public static string AppendToPrompt(string prompt, IReadOnlyList<MessageAttachment> attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        if (attachments.Count == 0)
        {
            return prompt;
        }

        var builder = new StringBuilder(prompt);
        builder.Append("\n\n---\n使用者附加了以下檔案，已放在目前的工作目錄（相對路徑）：\n");
        foreach (var attachment in attachments)
        {
            builder.Append(CultureInfo.InvariantCulture, $"- {attachment.Path}（{attachment.ContentType}，{FormatSize(attachment.Size)}）\n");
        }

        builder.Append("請依需要用工具讀取或處理這些檔案；若你能直接看到圖片，圖片也已附在這則訊息中。");
        return builder.ToString();
    }

    internal static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes} B"),
        < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0 / 1024.0:0.#} MB"),
    };
}
