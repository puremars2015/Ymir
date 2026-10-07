namespace Ymir.VibeMaker.Contracts.Attachments;

/// <summary>
/// 訊息附件（<c>POST /api/conversations/{id}/attachments</c> 的回應，也出現在 <c>MessageResponse.Attachments</c>）。
/// </summary>
/// <param name="Path">工作目錄內的相對路徑，可用 <c>/files/download?path=</c> 下載。</param>
/// <param name="ContentType">伺服器判斷的類型（圖片以檔頭判斷）。</param>
public sealed record AttachmentResponse(Guid Id, string FileName, string Path, string ContentType, long Size);
