namespace Ymir.VibeMaker.Application.Files;

/// <summary>
/// 把檔案寫進使用者 runtime 的工作目錄（使用者上傳的附件）。與 <see cref="IWorkspaceFileReader"/> 一樣在 runtime 內執行程序寫入，
/// Local / Podman / Docker / Remote 都適用，API 不直接存取 host 路徑（ADR-0007、ADR-0008）。
/// </summary>
public interface IWorkspaceFileWriter
{
    /// <summary>
    /// 寫入 <paramref name="relativePath"/>（必須通過 <see cref="WorkspacePathRules.IsSafeRelativePath"/>）。
    /// 先寫暫存檔、確認大小剛好 <paramref name="size"/> 才改名，中斷時不會留下半個檔案；
    /// 目標目錄經 realpath 解析後不在工作目錄內（symlink 逃逸）時拒絕。
    /// </summary>
    /// <returns>寫入成功為 true。</returns>
    Task<bool> WriteAsync(Guid userId, string workingDirectory, string relativePath, Stream content, long size, CancellationToken cancellationToken);
}
