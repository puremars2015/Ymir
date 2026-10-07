using Microsoft.AspNetCore.Http.Features;
using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.VibeMaker.Application.Attachments;
using Ymir.VibeMaker.Contracts.Attachments;
using Ymir.VibeMaker.Contracts.Executions;

namespace Ymir.Api.Endpoints;

/// <summary>
/// 上傳訊息附件：request body 就是檔案內容（不用 multipart，可以直接串流、沒有表單解析的額外負擔），檔名放在 query string。
/// 回傳的 id 在送出訊息時放進 <c>attachmentIds</c>。別人的對話一律 404（SA §12、§13）。
/// </summary>
internal static class AttachmentEndpoints
{
    public static IEndpointRouteBuilder MapAttachmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/conversations/{conversationId:guid}/attachments", UploadAsync)
            .WithName("UploadAttachment")
            .WithTags("Conversations")
            .RequireAntiforgeryHeader()
            .Accepts<Stream>("application/octet-stream")
            .Produces<AttachmentResponse>(StatusCodes.Status201Created);
        return endpoints;
    }

    private static async Task<IResult> UploadAsync(Guid conversationId, string? fileName, HttpContext context, AttachmentService service, CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength > AttachmentRules.MaxFileBytes)
        {
            return TooLarge();
        }

        // Kestrel 預設上限 30 MB；這個端點放寬到附件上限（多 1 byte 用來判斷超過）。
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = AttachmentRules.MaxFileBytes + 1;
        }

        // 先寫到暫存檔：才知道大小與檔頭，寫進 runtime 時也能確認大小完全一致；結束時自動刪除（與打包下載相同做法）。
        var temp = new FileStream(
            Path.Combine(Path.GetTempPath(), $"ymir-upload-{Guid.NewGuid():N}"),
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        await using (temp)
        {
            try
            {
                if (!await CopyWithLimitAsync(context.Request.Body, temp, AttachmentRules.MaxFileBytes, cancellationToken))
                {
                    return TooLarge();
                }
            }
            catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                return TooLarge();
            }

            var result = await service.UploadAsync(conversationId, fileName, temp, cancellationToken);
            return result.Outcome switch
            {
                AttachmentUploadOutcome.Uploaded => TypedResults.Created((string?)null, result.Attachment),
                AttachmentUploadOutcome.TooLarge => TooLarge(),
                AttachmentUploadOutcome.Invalid => ApiProblem.Create(StatusCodes.Status400BadRequest, ExecutionErrorCodes.AttachmentInvalid, "無法上傳這個檔案（檔名不合法或是空檔案）。"),
                _ => ConversationEndpoints.NotFound(),
            };
        }
    }

    private static IResult TooLarge() =>
        ApiProblem.Create(StatusCodes.Status413PayloadTooLarge, ExecutionErrorCodes.AttachmentTooLarge, $"檔案太大（上限 {AttachmentRules.MaxFileBytes / 1024 / 1024} MB）。");

    /// <returns>超過 <paramref name="maxBytes"/> 時回傳 false。</returns>
    private static async Task<bool> CopyWithLimitAsync(Stream source, Stream target, long maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                return false;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        await target.FlushAsync(cancellationToken);
        return true;
    }
}
