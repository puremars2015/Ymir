namespace Ymir.VibeMaker.Application.Runtime;

/// <summary>要在 runtime 內啟動的程序。路徑皆為 runtime 內部路徑（例如 <c>/workspace</c>），不是 host 路徑。</summary>
public sealed record RuntimeProcessSpec(
    string Executable,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string>? Environment = null);
