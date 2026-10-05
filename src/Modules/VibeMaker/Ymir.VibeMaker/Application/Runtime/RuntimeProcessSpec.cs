namespace Ymir.VibeMaker.Application.Runtime;

/// <summary>要在 runtime 內啟動的程序。路徑皆為 runtime 內部路徑（例如 <c>/workspace</c>），不是 host 路徑。</summary>
/// <param name="WorkingDirectory">
/// 程序的工作目錄，必須通過 <see cref="RuntimePaths.IsAllowedWorkingDirectory"/>；不存在時由 runtime manager 建立。
/// </param>
public sealed record RuntimeProcessSpec(
    string Executable,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string>? Environment = null,
    string WorkingDirectory = RuntimePaths.Workspace);
