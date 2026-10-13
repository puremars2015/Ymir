namespace Ymir.VibeMaker.Application.Models;

/// <summary>
/// 模型名稱與供應商（provider）：管理介面要知道模型由哪個 provider 提供（例如 minimax.io、OpenRouter、ChatGPT OAuth），
/// 使用者只需要知道是哪一個模型，所以兩者分開保存，只有管理端的 API 回傳 provider（ADR-0020 補充）。
/// </summary>
public static class ModelNames
{
    /// <summary>
    /// 整理部署設定的顯示名稱與 provider。沒有設定 <c>Provider</c> 時，相容舊設定把 provider 寫在名稱結尾括號的寫法，
    /// 例如「GPT-6.1 Sol (ChatGPT OAuth)」拆成名稱「GPT-6.1 Sol」與 provider「ChatGPT OAuth」，已部署的設定不必立即修改。
    /// </summary>
    public static (string DisplayName, string? Provider) Normalize(string id, string? displayName, string? provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var name = displayName?.Trim();
        var configured = provider?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return (id, string.IsNullOrEmpty(configured) ? null : configured);
        }

        if (!string.IsNullOrEmpty(configured))
        {
            return (name, configured);
        }

        return TrySplitSuffix(name, out var baseName, out var suffix) ? (baseName, suffix) : (name, null);
    }

    /// <summary>
    /// 使用者看到的名稱：只有模型名稱；同一份清單裡有同名模型（例如同一個模型分別經 OpenRouter 與 ChatGPT OAuth 開放）時，
    /// 這幾個才加上 provider，否則使用者無從分辨。順序與輸入相同。
    /// </summary>
    public static IReadOnlyList<string> ForUsers(IReadOnlyList<ModelDescriptor> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        var duplicated = models.GroupBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. models.Select(m => duplicated.Contains(m.DisplayName) && !string.IsNullOrEmpty(m.Provider)
            ? $"{m.DisplayName} ({m.Provider})"
            : m.DisplayName)];
    }

    /// <summary>名稱結尾的「(X)」或全形「（X）」；括號前必須還有名稱，括號內不可空白。</summary>
    private static bool TrySplitSuffix(string name, out string baseName, out string suffix)
    {
        baseName = name;
        suffix = string.Empty;
        var close = name[^1];
        var open = close switch
        {
            ')' => '(',
            '）' => '（',
            _ => '\0',
        };
        var start = open == '\0' ? -1 : name.LastIndexOf(open);
        if (start <= 0)
        {
            return false;
        }

        var inner = name[(start + 1)..^1].Trim();
        var prefix = name[..start].Trim();
        if (inner.Length == 0 || prefix.Length == 0)
        {
            return false;
        }

        baseName = prefix;
        suffix = inner;
        return true;
    }
}
