namespace Ymir.VibeMaker.Application.Files;

/// <summary>
/// 讀取使用者 runtime 工作目錄內的檔案（Agent 產生的成果）。實作在 Infrastructure：
/// 經 <see cref="Runtime.IAgentRuntimeManager"/> 在 runtime 內執行程序讀取，所以 Local / Podman / Docker / Remote 都一樣，
/// API 不需要直接存取 host 路徑（ADR-0007、ADR-0008）。
/// </summary>
public interface IWorkspaceFileReader
{
    /// <summary>列出工作目錄內的一般檔案（不含隱藏檔、node_modules、symlink），最多 <paramref name="limit"/> + 1 筆。</summary>
    Task<IReadOnlyList<WorkspaceFileEntry>> ListAsync(Guid userId, string workingDirectory, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// 依序讀取檔案，每個存在的檔案呼叫一次 <paramref name="onFile"/>（content 剛好是該檔案的 size 個位元組，callback 結束後未讀完的部分會被略過）。
    /// 不存在、不是一般檔案、或解析後不在工作目錄內（<c>..</c>、symlink 逃逸）的路徑直接略過。
    /// </summary>
    Task ReadAsync(
        Guid userId,
        string workingDirectory,
        IReadOnlyList<string> relativePaths,
        Func<WorkspaceFileContent, CancellationToken, Task> onFile,
        CancellationToken cancellationToken);
}

public sealed record WorkspaceFileEntry(string Path, long Size, DateTimeOffset ModifiedAt);

public sealed record WorkspaceFileContent(string Path, long Size, Stream Content);
