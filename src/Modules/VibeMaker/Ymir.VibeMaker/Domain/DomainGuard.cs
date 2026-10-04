namespace Ymir.VibeMaker.Domain;

internal static class DomainGuard
{
    public static string RequiredText(string? value, int maxLength, string parameterName)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new DomainValidationException($"{parameterName} is required.");
        }

        if (trimmed.Length > maxLength)
        {
            throw new DomainValidationException($"{parameterName} must be at most {maxLength} characters.");
        }

        return trimmed;
    }
}

/// <summary>輸入不符合領域規則（API 轉為 400）。</summary>
public sealed class DomainValidationException(string message) : Exception(message);
