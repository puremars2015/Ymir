namespace Ymir.VibeMaker.Contracts.Files;

/// <param name="Path">相對於對話工作目錄的路徑（以 <c>/</c> 分隔）。</param>
public sealed record WorkspaceFileResponse(string Path, long Size, DateTimeOffset ModifiedAt);

/// <param name="Truncated">檔案太多時只列出前面的部分。</param>
public sealed record WorkspaceFilesResponse(List<WorkspaceFileResponse> Files, bool Truncated);
