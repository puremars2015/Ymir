using System.Text;

namespace Ymir.VibeMaker.Application.Knowledge;

/// <summary>
/// 切段（ADR-0014 §5）：依段落與句尾邊界合併成約 <c>size</c> 字元的段落，相鄰段落重疊約 <c>overlap</c> 字元；
/// 單一段落過長時在句尾（。！？.!?）或硬切。每段保留來源頁碼（不跨頁合併，引用的頁碼才正確）。
/// </summary>
public static class TextChunker
{
    public const int StrategyVersion = 1;

    private static readonly System.Buffers.SearchValues<char> s_sentenceEnds = System.Buffers.SearchValues.Create("。！？.!?\n");

    public static IReadOnlyList<TextChunk> Chunk(IReadOnlyList<(int? Page, string Text)> pages, int size, int overlap)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (size <= 0 || overlap < 0 || overlap >= size)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        var chunks = new List<TextChunk>();
        foreach (var (page, text) in pages)
        {
            var current = new StringBuilder();
            foreach (var paragraph in Paragraphs(text))
            {
                foreach (var piece in Split(paragraph, size))
                {
                    if (current.Length > 0 && current.Length + piece.Length + 1 > size)
                    {
                        Emit(chunks, page, current, overlap);
                    }

                    if (current.Length > 0)
                    {
                        current.Append('\n');
                    }

                    current.Append(piece);
                }
            }

            if (current.Length > 0)
            {
                chunks.Add(new TextChunk(chunks.Count, page, current.ToString().Trim()));
            }
        }

        return chunks;
    }

    private static void Emit(List<TextChunk> chunks, int? page, StringBuilder current, int overlap)
    {
        var text = current.ToString().Trim();
        chunks.Add(new TextChunk(chunks.Count, page, text));
        current.Clear();
        if (overlap > 0 && text.Length > overlap)
        {
            // 從重疊範圍內最近的句尾之後開始，避免下一段以半句開頭。
            var tail = text[^overlap..];
            var boundary = tail.AsSpan().IndexOfAny(s_sentenceEnds);
            current.Append(boundary >= 0 && boundary < tail.Length - 1 ? tail[(boundary + 1)..].TrimStart() : tail);
        }
    }

    private static IEnumerable<string> Paragraphs(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => string.Join(' ', p.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)))
            .Where(p => p.Length > 0);

    /// <summary>過長的段落在句尾切開；沒有句尾時硬切。</summary>
    private static IEnumerable<string> Split(string paragraph, int size)
    {
        var rest = paragraph;
        while (rest.Length > size)
        {
            var cut = rest.AsSpan(0, size).LastIndexOfAny(s_sentenceEnds);
            var end = cut >= size / 2 ? cut + 1 : size;
            yield return rest[..end].Trim();
            rest = rest[end..].TrimStart();
        }

        if (rest.Length > 0)
        {
            yield return rest;
        }
    }
}
