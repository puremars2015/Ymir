using System.IO.Compression;
using System.Text;
using System.Xml;
using UglyToad.PdfPig;
using Ymir.VibeMaker.Application.Knowledge;

namespace Ymir.VibeMaker.Infrastructure.Knowledge;

/// <summary>
/// 擷取文件文字（ADR-0014 §5）：TXT / Markdown（UTF-8）、文字型 PDF（PdfPig，逐頁）、DOCX（只讀 <c>word/document.xml</c> 的段落）。
/// 文件視為不可信資料：不執行巨集、不解析外部關聯、XML 不處理 DTD；失敗時只回摘要。
/// </summary>
internal sealed class DocumentTextExtractor : IDocumentTextExtractor
{
    /// <summary>DOCX 解壓後的 document.xml 上限，避免壓縮炸彈。</summary>
    private const long MaxDocxXmlBytes = 50L * 1024 * 1024;

    public IReadOnlyList<(int? Page, string Text)> Extract(string fileName, Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        try
        {
            return extension switch
            {
                ".txt" or ".md" or ".markdown" => [(null, ReadText(content))],
                ".pdf" => ReadPdf(content),
                ".docx" => [(null, ReadDocx(content))],
                _ => throw new KnowledgeException("不支援的檔案格式。"),
            };
        }
        catch (KnowledgeException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or XmlException or InvalidOperationException or ArgumentException or FormatException)
        {
            throw new KnowledgeException("無法讀取文件內容，檔案可能已損毀或受密碼保護。", ex);
        }
    }

    private static string ReadText(Stream content)
    {
        using var reader = new StreamReader(content, new UTF8Encoding(false, throwOnInvalidBytes: false), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static List<(int? Page, string Text)> ReadPdf(Stream content)
    {
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        using var document = PdfDocument.Open(buffer.ToArray());
        return [.. document.GetPages().Select(page => ((int?)page.Number, page.Text)).Where(p => !string.IsNullOrWhiteSpace(p.Text))];
    }

    private static string ReadDocx(Stream content)
    {
        using var archive = new ZipArchive(content, ZipArchiveMode.Read);
        var entry = archive.GetEntry("word/document.xml") ?? throw new KnowledgeException("DOCX 檔案缺少文件內容。");
        if (entry.Length > MaxDocxXmlBytes)
        {
            throw new KnowledgeException("文件內容過大。");
        }

        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        const string word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var text = new StringBuilder();
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.NamespaceURI == word)
            {
                switch (reader.LocalName)
                {
                    case "t":
                        text.Append(reader.ReadElementContentAsString());
                        break;
                    case "tab":
                        text.Append('\t');
                        break;
                    case "br":
                        text.Append('\n');
                        break;
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "p" && reader.NamespaceURI == word)
            {
                text.Append("\n\n");
            }
        }

        return text.ToString();
    }
}
