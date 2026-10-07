using System.IO.Compression;
using System.Text;
using Microsoft.Net.Http.Headers;
using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.VibeMaker.Application.Files;
using Ymir.VibeMaker.Contracts.Files;

namespace Ymir.Api.Endpoints;

/// <summary>
/// 對話的檔案（Agent 產生的成果）：列出、下載單檔、打包下載。別人的對話一律 404（SA §12、§13）。
/// <para>
/// 下載內容是 Agent 產生的、不可信任的檔案（例如 HTML）：一律以附件下載（<c>Content-Disposition: attachment</c>、
/// <c>application/octet-stream</c>、<c>nosniff</c>、CSP <c>sandbox</c>），不在 Ymir 的網域上直接開啟，避免以 Ymir 的 cookie 執行其中的腳本。
/// </para>
/// </summary>
internal static class ArtifactEndpoints
{
    public static IEndpointRouteBuilder MapArtifactEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/conversations/{conversationId:guid}/artifacts").WithTags("Conversations").RequireAntiforgeryHeader();
        group.MapGet("/", async Task<IResult> (Guid conversationId, ArtifactService service, CancellationToken ct) =>
            await service.ListAsync(conversationId, ct) is { } artifacts ? TypedResults.Ok(artifacts) : ConversationEndpoints.NotFound())
            .WithName("ListConversationArtifacts").Produces<List<ArtifactGroupResponse>>();
        group.MapGet("/{executionId:guid}/download", DownloadAsync).WithName("DownloadArtifact").Produces(200, contentType: "application/octet-stream");
        group.MapGet("/{executionId:guid}/archive", ArchiveAsync).WithName("DownloadArtifactArchive").Produces(200, contentType: "application/zip");

        return endpoints;
    }

    private static async Task<IResult> DownloadAsync(Guid conversationId, Guid executionId, string? path, HttpContext context, ArtifactService service, CancellationToken cancellationToken)
    {
        var outcome = await service.ReadFileAsync(conversationId, executionId, path, async (file, ct) =>
        {
            PrepareDownload(context.Response, WorkspacePathRules.FileName(file.Path), "application/octet-stream");
            context.Response.ContentLength = file.Size;
            await file.Content.CopyToAsync(context.Response.Body, ct);
        }, cancellationToken);

        return outcome switch
        {
            WorkspaceFileOutcome.Success => Results.Empty,
            WorkspaceFileOutcome.TooLarge => ApiProblem.Create(StatusCodes.Status413PayloadTooLarge, "FILE_TOO_LARGE", "檔案太大，無法下載。"),
            _ => ApiProblem.Create(StatusCodes.Status404NotFound, "FILE_NOT_FOUND", "找不到檔案。"),
        };
    }

    private static async Task<IResult> ArchiveAsync(Guid conversationId, Guid executionId, HttpContext context, ArtifactService service, CancellationToken cancellationToken)
    {
        var plan = await service.PlanAsync(conversationId, executionId, cancellationToken);
        switch (plan.Outcome)
        {
            case WorkspaceFileOutcome.NotFound:
                return ConversationEndpoints.NotFound();
            case WorkspaceFileOutcome.TooLarge:
                return ApiProblem.Create(
                    StatusCodes.Status413PayloadTooLarge,
                    "ARCHIVE_TOO_LARGE",
                    $"檔案太多或太大（上限 {WorkspaceFileService.MaxArchiveFiles} 個、{WorkspaceFileService.MaxArchiveBytes / 1024 / 1024} MB），請改為個別下載。");
        }

        // ZipArchive 寫入 local header 時仍有同步 I/O（Kestrel 禁止），所以先寫到暫存檔，再非同步送出；結束時自動刪除。
        var temp = new FileStream(
            Path.Combine(Path.GetTempPath(), $"ymir-archive-{Guid.NewGuid():N}.zip"),
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        await using (temp)
        {
            using (var archive = new ZipArchive(temp, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: Encoding.UTF8))
            {
                await service.ReadArchiveAsync(plan, executionId, async (file, ct) =>
                {
                    var entry = archive.CreateEntry(file.Path, CompressionLevel.Fastest);
                    var target = await entry.OpenAsync(ct);
                    await using (target)
                    {
                        await file.Content.CopyToAsync(target, ct);
                    }
                }, cancellationToken);
            }

            PrepareDownload(context.Response, ArchiveName(plan.Title), "application/zip");
            context.Response.ContentLength = temp.Length;
            temp.Position = 0;
            await temp.CopyToAsync(context.Response.Body, cancellationToken);
        }

        return Results.Empty;
    }

    private static void PrepareDownload(HttpResponse response, string fileName, string contentType)
    {
        response.ContentType = contentType;
        // filename=（ASCII 備援，非 ASCII 以 _ 取代）+ filename*=UTF-8''（完整檔名），各瀏覽器都能取得正確檔名。
        var disposition = new ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName(fileName);
        response.Headers.ContentDisposition = disposition.ToString();
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.ContentSecurityPolicy = "sandbox";
        response.Headers.CacheControl = "no-store";
    }

    /// <summary>zip 檔名：對話標題去掉檔名不允許的字元。</summary>
    internal static string ArchiveName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var name = new string([.. (title ?? string.Empty).Trim().Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c)]);
        if (name.Length > 80)
        {
            name = name[..80];
        }

        return (string.IsNullOrWhiteSpace(name) ? "files" : name) + ".zip";
    }
}
