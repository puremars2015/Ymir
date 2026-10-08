namespace Ymir.Testing.FakeLlm;

/// <summary>固定、可重現的假 embedding：字元 bigram 雜湊到固定維度後 L2 正規化（不呼叫任何真實模型）。</summary>
public static class FakeEmbedding
{
    public const int Dimensions = 256;

    public static float[] Embed(string text)
    {
        var vector = new float[Dimensions];
        var normalized = (text ?? string.Empty).ToLowerInvariant();
        for (var i = 0; i + 1 < normalized.Length; i++)
        {
            if (char.IsWhiteSpace(normalized[i]) || char.IsWhiteSpace(normalized[i + 1]))
            {
                continue;
            }

            var hash = (uint)((normalized[i] * 31) ^ (normalized[i + 1] * 131071));
            vector[hash % Dimensions] += 1;
        }

        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm > 0)
        {
            for (var i = 0; i < Dimensions; i++)
            {
                vector[i] /= norm;
            }
        }

        return vector;
    }
}
