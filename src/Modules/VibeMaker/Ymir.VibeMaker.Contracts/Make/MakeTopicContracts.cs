namespace Ymir.VibeMaker.Contracts.Make;

/// <summary><c>GET /api/make-topics</c>：<c>/make</c> 的主題按鈕（只列啟用中的，依排序）。不含給 Agent 的建置指示。</summary>
public sealed record MakeTopicResponse(Guid Id, string Name, string? Description, int SortOrder);

/// <summary>Admin 看到的主題（含建置指示與啟用狀態）。</summary>
public sealed record AdminMakeTopicResponse(
    Guid Id,
    string Name,
    string? Description,
    string Instructions,
    int SortOrder,
    bool IsEnabled,
    DateTimeOffset UpdatedAt);

/// <summary>新增 / 修改主題（Admin）。</summary>
/// <param name="Name">1～50 字。</param>
/// <param name="Description">顯示在按鈕上的說明，200 字以內。</param>
/// <param name="Instructions">給 Agent 的建置指示，1～4000 字。</param>
public sealed record SaveMakeTopicRequest(string Name, string? Description, string Instructions, int SortOrder, bool IsEnabled);
