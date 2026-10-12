using System.Text.Json;
using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.Platform.Auditing;
using Ymir.Platform.Identity;
using Ymir.Platform.Settings;

namespace Ymir.Api.Endpoints;

/// <summary>
/// 公司識別（公司名稱與 favicon）：Ymir 是開源專案名稱，不可改；公司名稱由 Admin 在「系統設定」輸入
/// （ADR-0010 同一套 system_settings），畫面顯示為「{公司名稱} Ymir」，留空則只顯示 Ymir。
/// 登入頁也要顯示，所以讀取端點是匿名的；內容都是公開資訊（名稱、圖示），不含任何機密。
/// </summary>
internal static class BrandingEndpoints
{
    public const string DocumentKey = "branding.document";
    public const string FaviconKey = "branding.favicon";
    public const int MaxNameLength = 40;
    public const int MaxFaviconBytes = 256 * 1024;

    public static IEndpointRouteBuilder MapBrandingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/branding", GetAsync).WithName("GetBranding").WithTags("Branding").AllowAnonymous().Produces<BrandingResponse>();
        endpoints.MapGet("/api/branding/favicon", GetFaviconAsync).WithName("GetBrandingFavicon").WithTags("Branding").AllowAnonymous()
            .Produces(StatusCodes.Status200OK, contentType: "image/png").Produces(StatusCodes.Status404NotFound);

        var admin = endpoints.MapGroup("/api/admin/settings/branding").WithTags("Admin").RequireAuthorization(AuthSetup.AdminPolicy).RequireAntiforgeryHeader();
        admin.MapPut("/", SaveAsync).WithName("AdminSaveBranding").Produces<BrandingResponse>();
        admin.MapPut("/favicon", UploadFaviconAsync).WithName("AdminUploadFavicon").Accepts<Stream>("application/octet-stream").Produces<BrandingResponse>();
        admin.MapDelete("/favicon", DeleteFaviconAsync).WithName("AdminDeleteFavicon").Produces<BrandingResponse>();
        return endpoints;
    }

    private static async Task<BrandingResponse> GetAsync(ISystemSettingsStore store, CancellationToken cancellationToken) =>
        await LoadAsync(store, cancellationToken);

    private static async Task<IResult> GetFaviconAsync(HttpContext context, ISystemSettingsStore store, CancellationToken cancellationToken)
    {
        var stored = await store.GetAsync(FaviconKey, cancellationToken);
        if (stored is null || !TryDecode(stored.Value, out var contentType, out var bytes))
        {
            return TypedResults.NotFound();
        }

        // 圖片本身不可執行，但仍禁止 sniff 並以 sandbox 限制，避免被當成頁面開啟。
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        context.Response.Headers.CacheControl = "public, max-age=300";
        return TypedResults.File(bytes, contentType);
    }

    private static async Task<IResult> SaveAsync(
        SaveBrandingRequest request,
        ISystemSettingsStore store,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var name = request.CompanyName?.Trim() ?? string.Empty;
        if (name.Length > MaxNameLength || HasControlCharacters(name))
        {
            return ApiProblem.Create(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", $"公司名稱不超過 {MaxNameLength} 個字，且不可包含控制字元。");
        }

        // 留空表示不顯示公司名稱：刪除設定即可。
        if (name.Length == 0)
        {
            await store.DeleteAsync([DocumentKey], cancellationToken);
        }
        else
        {
            await store.SetAsync(DocumentKey, JsonSerializer.Serialize(new BrandingDocument(name)), currentUser.ActorName, cancellationToken);
        }

        await WriteAuditAsync(auditLog, currentUser, timeProvider, "admin.settings.branding.update", DocumentKey, cancellationToken);
        return TypedResults.Ok(await LoadAsync(store, cancellationToken));
    }

    private static async Task<IResult> UploadFaviconAsync(
        HttpContext context,
        ISystemSettingsStore store,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await context.Request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxFaviconBytes)
            {
                return ApiProblem.Create(StatusCodes.Status413PayloadTooLarge, "VALIDATION_FAILED", $"圖示太大（上限 {MaxFaviconBytes / 1024} KB）。");
            }

            buffer.Write(chunk, 0, read);
        }

        var bytes = buffer.ToArray();
        // 以檔頭判斷格式，不信任 Content-Type；不收 SVG（可含腳本）。
        var contentType = Sniff(bytes);
        if (contentType is null)
        {
            return ApiProblem.Create(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", "圖示格式不支援，請使用 PNG、ICO、JPEG 或 WebP。");
        }

        await store.SetAsync(FaviconKey, $"{contentType};{Convert.ToBase64String(bytes)}", currentUser.ActorName, cancellationToken);
        await WriteAuditAsync(auditLog, currentUser, timeProvider, "admin.settings.branding.favicon", FaviconKey, cancellationToken);
        return TypedResults.Ok(await LoadAsync(store, cancellationToken));
    }

    private static async Task<IResult> DeleteFaviconAsync(
        ISystemSettingsStore store,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        await store.DeleteAsync([FaviconKey], cancellationToken);
        await WriteAuditAsync(auditLog, currentUser, timeProvider, "admin.settings.branding.favicon_reset", FaviconKey, cancellationToken);
        return TypedResults.Ok(await LoadAsync(store, cancellationToken));
    }

    private static async Task<BrandingResponse> LoadAsync(ISystemSettingsStore store, CancellationToken cancellationToken)
    {
        var name = string.Empty;
        if (await store.GetAsync(DocumentKey, cancellationToken) is { } document)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<BrandingDocument>(document.Value);
                if (parsed is not null && !string.IsNullOrWhiteSpace(parsed.CompanyName) && parsed.CompanyName.Length <= MaxNameLength)
                {
                    name = parsed.CompanyName;
                }
            }
            catch (JsonException)
            {
                // 資料損毀時退回預設值，不讓登入頁壞掉。
            }
        }

        var favicon = await store.GetAsync(FaviconKey, cancellationToken);
        return new BrandingResponse(name, favicon is null ? null : $"/api/branding/favicon?v={favicon.UpdatedAt.ToUnixTimeSeconds()}");
    }

    private static Task WriteAuditAsync(IAuditLog auditLog, ICurrentUser currentUser, TimeProvider timeProvider, string action, string target, CancellationToken cancellationToken) =>
        auditLog.WriteAsync(new AuditEntry(currentUser.ActorName, action, "setting", target, AuditResult.Success, timeProvider.GetUtcNow(), null), cancellationToken);

    private static bool HasControlCharacters(string value) => value.Any(char.IsControl);

    internal static string? Sniff(ReadOnlySpan<byte> b) => b switch
    {
        [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
        [0x00, 0x00, 0x01, 0x00, ..] => "image/x-icon",
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        _ => null,
    };

    private static bool TryDecode(string stored, out string contentType, out byte[] bytes)
    {
        contentType = string.Empty;
        bytes = [];
        var separator = stored.IndexOf(';', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(stored[(separator + 1)..]);
        }
        catch (FormatException)
        {
            return false;
        }

        // 讀出時再驗一次，資料庫被改壞也不會送出非圖片內容。
        contentType = Sniff(bytes) ?? string.Empty;
        return contentType.Length > 0;
    }

    private sealed record BrandingDocument(string CompanyName);
}

public sealed record BrandingResponse(string CompanyName, string? FaviconUrl);

public sealed record SaveBrandingRequest(string? CompanyName);
