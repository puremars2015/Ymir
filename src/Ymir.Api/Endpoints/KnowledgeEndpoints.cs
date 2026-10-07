using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.Platform.Identity;
using Ymir.VibeMaker.Application.Knowledge;
using Ymir.VibeMaker.Application.Models;
using Ymir.VibeMaker.Domain;

namespace Ymir.Api.Endpoints;

/// <summary>
/// 專案知識庫（ADR-0014）：上傳、列出、移除、重試。只能存取自己的專案（別人的專案一律 404，SA §12）；
/// 上傳的 body 就是檔案內容，檔名放在 query string（同附件上傳）。
/// </summary>
internal static class KnowledgeEndpoints
{
    public static IEndpointRouteBuilder MapKnowledgeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/projects/{projectId:guid}/knowledge").WithTags("Knowledge").RequireAntiforgeryHeader();
        group.MapGet("/", GetAsync).WithName("GetKnowledgeBase");
        group.MapPost("/documents", UploadAsync).WithName("UploadKnowledgeDocument")
            .Accepts<Stream>("application/octet-stream")
            .Produces<KnowledgeDocumentResponse>(StatusCodes.Status201Created);
        group.MapDelete("/documents/{documentId:guid}", RemoveAsync).WithName("RemoveKnowledgeDocument");
        group.MapPost("/documents/{documentId:guid}/retry", RetryAsync).WithName("RetryKnowledgeDocument");
        group.MapPost("/ask", AskAsync).WithName("AskKnowledgeBase").Produces<KnowledgeAnswerResponse>();
        return endpoints;
    }

    private static async Task<IResult> AskAsync(
        Guid projectId,
        AskKnowledgeRequest request,
        ICurrentUser currentUser,
        KnowledgeQueryService service,
        CancellationToken cancellationToken)
    {
        (KnowledgeAnswer? Answer, KnowledgeAskError Error)? result;
        try
        {
            result = await service.AskAsync(currentUser.UserId, projectId, request.Question, request.ModelId, currentUser.ActorName, cancellationToken);
        }
        catch (KnowledgeException ex)
        {
            return ApiProblem.Create(StatusCodes.Status502BadGateway, "KNOWLEDGE_UPSTREAM", ex.Message);
        }
        catch (ModelCredentialException)
        {
            return ApiProblem.Create(StatusCodes.Status502BadGateway, "KNOWLEDGE_UPSTREAM", "暫時無法連線到模型服務，請稍後再試。");
        }

        return result switch
        {
            null => TypedResults.NotFound(),
            ({ } answer, _) => TypedResults.Ok(KnowledgeAnswerResponse.From(answer)),
            (_, KnowledgeAskError.Disabled) => ApiProblem.Create(StatusCodes.Status409Conflict, "KNOWLEDGE_DISABLED", "知識庫尚未設定（需要 Embedding 模型），請洽管理員。"),
            _ => ApiProblem.Create(StatusCodes.Status400BadRequest, "KNOWLEDGE_QUESTION_INVALID", $"請輸入問題（最多 {KnowledgeQueryService.MaxQuestionLength} 字）。"),
        };
    }

    private static async Task<Results<Ok<KnowledgeBaseResponse>, NotFound>> GetAsync(
        Guid projectId,
        ICurrentUser currentUser,
        KnowledgeBaseService service,
        CancellationToken cancellationToken) =>
        await service.ListAsync(currentUser.UserId, projectId, cancellationToken) is { } documents
            ? TypedResults.Ok(new KnowledgeBaseResponse(
                service.IsEnabled,
                service.Options.MaxFileBytes,
                service.Options.MaxDocuments,
                KnowledgeBaseService.SupportedExtensions,
                [.. documents.Select(KnowledgeDocumentResponse.From)]))
            : TypedResults.NotFound();

    private static async Task<IResult> UploadAsync(
        Guid projectId,
        string? fileName,
        HttpContext context,
        ICurrentUser currentUser,
        KnowledgeBaseService service,
        CancellationToken cancellationToken)
    {
        var maxBytes = service.Options.MaxFileBytes;
        if (context.Request.ContentLength > maxBytes)
        {
            return TooLarge(maxBytes);
        }

        // Kestrel 預設上限 30 MB；放寬到知識庫的單檔上限（多 1 byte 用來判斷超過），超過時由檔案儲存回報。
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = maxBytes + 1;
        }

        (KnowledgeDocument? Document, KnowledgeUploadError Error)? result;
        try
        {
            result = await service.UploadAsync(currentUser.UserId, projectId, fileName, context.Request.Body, currentUser.ActorName, cancellationToken);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return TooLarge(maxBytes);
        }

        return result switch
        {
            null => TypedResults.NotFound(),
            ({ } document, _) => TypedResults.Created((string?)null, KnowledgeDocumentResponse.From(document)),
            (_, KnowledgeUploadError.Disabled) => ApiProblem.Create(StatusCodes.Status409Conflict, "KNOWLEDGE_DISABLED", "知識庫尚未設定（需要 Embedding 模型），請洽管理員。"),
            (_, KnowledgeUploadError.TooLarge) => TooLarge(maxBytes),
            (_, KnowledgeUploadError.TooManyDocuments) => ApiProblem.Create(StatusCodes.Status409Conflict, "KNOWLEDGE_LIMIT", $"每個專案最多 {service.Options.MaxDocuments} 份文件。"),
            _ => ApiProblem.Create(StatusCodes.Status400BadRequest, "KNOWLEDGE_UNSUPPORTED", $"只支援 {string.Join("、", KnowledgeBaseService.SupportedExtensions)} 檔案。"),
        };
    }

    private static async Task<IResult> RemoveAsync(
        Guid projectId,
        Guid documentId,
        ICurrentUser currentUser,
        KnowledgeBaseService service,
        CancellationToken cancellationToken) =>
        await service.RemoveAsync(currentUser.UserId, projectId, documentId, currentUser.ActorName, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

    private static async Task<Results<Ok<KnowledgeDocumentResponse>, NotFound>> RetryAsync(
        Guid projectId,
        Guid documentId,
        ICurrentUser currentUser,
        KnowledgeBaseService service,
        CancellationToken cancellationToken) =>
        await service.RetryAsync(currentUser.UserId, projectId, documentId, cancellationToken) is { } document
            ? TypedResults.Ok(KnowledgeDocumentResponse.From(document))
            : TypedResults.NotFound();

    private static IResult TooLarge(long maxBytes) =>
        ApiProblem.Create(StatusCodes.Status413PayloadTooLarge, "KNOWLEDGE_TOO_LARGE", $"檔案太大（上限 {maxBytes / 1024 / 1024} MB）。");
}

/// <param name="Enabled">是否設定了 Embedding 模型；沒有時不能上傳。</param>
public sealed record KnowledgeBaseResponse(
    bool Enabled,
    long MaxFileBytes,
    int MaxDocuments,
    IReadOnlyList<string> SupportedExtensions,
    IReadOnlyList<KnowledgeDocumentResponse> Documents);

/// <param name="Error">失敗原因的摘要（不含路徑或例外細節）。</param>
public sealed record KnowledgeDocumentResponse(
    Guid Id,
    string FileName,
    int Version,
    long Size,
    KnowledgeDocumentStatus Status,
    int ChunkCount,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    internal static KnowledgeDocumentResponse From(KnowledgeDocument document) =>
        new(document.Id, document.FileName, document.Version, document.Size, document.Status, document.ChunkCount, document.Error, document.CreatedAt, document.UpdatedAt);
}

public sealed record AskKnowledgeRequest(string? Question, string? ModelId);

/// <param name="Answer">模型的回答；資料不足或模型未獲允許時為 null。</param>
/// <param name="InsufficientData">文件沒有涵蓋這個問題（沒有呼叫模型）。</param>
/// <param name="ModelAllowed">選用的模型是否可用於知識庫；false 時只回檢索到的段落。</param>
public sealed record KnowledgeAnswerResponse(
    string? Answer,
    bool InsufficientData,
    bool ModelAllowed,
    string ModelId,
    IReadOnlyList<KnowledgeCitationResponse> Citations)
{
    internal static KnowledgeAnswerResponse From(KnowledgeAnswer answer) =>
        new(answer.Answer, answer.InsufficientData, answer.ModelAllowed, answer.ModelId,
            [.. answer.Citations.Select(c => new KnowledgeCitationResponse(c.Number, c.DocumentId, c.FileName, c.Version, c.Ordinal, c.Page, c.Excerpt, c.Score))]);
}

public sealed record KnowledgeCitationResponse(int Number, Guid DocumentId, string FileName, int Version, int Ordinal, int? Page, string Excerpt, double Score);
