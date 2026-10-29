namespace Ymir.VibeMaker.Application.Connectors.OneDrive;

/// <summary>OneDrive 名稱與工作目錄路徑的規則（ADR-0030）。只接受使用者或 Agent 輸入的名稱，由後端在使用者自己的 drive 解析。</summary>
public static class OneDrivePaths
{
    public const int MaxSegmentLength = 100;

    private const string InvalidChars = "\"*:<>?\\|";

    private static readonly System.Buffers.SearchValues<char> s_invalidChars = System.Buffers.SearchValues.Create(InvalidChars);

    public static bool IsValidName(string name) =>
        name.Length is > 0 and <= MaxSegmentLength
        && name is not ("." or "..")
        && !name.EndsWith('.')
        && name.IndexOfAny(s_invalidChars) < 0
        && !name.Any(char.IsControl);

    /// <summary>工作目錄頂層的 <c>onedrive/</c>：OneDrive 外掛的 <c>download</c> 工具放檔案的地方（ADR-0030 §4）。</summary>
    public const string AgentDirectory = "onedrive";
}
