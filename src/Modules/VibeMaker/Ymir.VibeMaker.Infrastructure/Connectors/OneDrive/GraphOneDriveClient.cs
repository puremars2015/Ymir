using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ymir.VibeMaker.Application.Connectors.OneDrive;

namespace Ymir.VibeMaker.Infrastructure.Connectors.OneDrive;

/// <summary>
/// Microsoft Graph 的 OneDrive 操作（ADR-0013）。只在後端呼叫；access token 由呼叫端提供、不保存。
/// 429 / 503 依 <c>Retry-After</c> 重試（最多 3 次、每次最多 30 秒）；錯誤訊息只有摘要，原始回應只記 status 與 error code。
/// </summary>
internal sealed partial class GraphOneDriveClient(HttpClient http, IOptions<OneDriveOptions> options, ILogger<GraphOneDriveClient> logger) : IOneDriveClient
{
    public const string HttpClientName = "Ymir.OneDrive.Graph";
    public const int MaxRetries = 3;

    /// <summary>Graph 的簡單上傳上限是 4 MB，更大的檔案用 upload session。</summary>
    public const long SimpleUploadLimit = 4L * 1024 * 1024;

    /// <summary>upload session 每段必須是 320 KiB 的倍數（Graph 文件）；5 MiB = 16 × 320 KiB。</summary>
    public const int UploadChunkSize = 16 * 320 * 1024;

    private const int PageSize = 200;

    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    private readonly string _baseUrl = options.Value.GraphBaseUrl.ToString().TrimEnd('/');

    public async Task<GraphUser> GetMeAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(accessToken, "/me", cancellationToken).ConfigureAwait(false)
            ?? throw new OneDriveException("無法讀取 Microsoft 帳號資訊。");
        var root = document.RootElement;
        return new GraphUser(root.GetProperty("id").GetString()!, root.TryGetProperty("userPrincipalName", out var upn) ? upn.GetString() ?? string.Empty : string.Empty);
    }

    public async Task<string> GetDriveIdAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(accessToken, "/me/drive", cancellationToken).ConfigureAwait(false)
            ?? throw new OneDriveException("這個帳號沒有可用的 OneDrive。");
        return document.RootElement.GetProperty("id").GetString()!;
    }

    public async Task<DriveItemInfo> GetRootAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(accessToken, "/me/drive/root", cancellationToken).ConfigureAwait(false)
            ?? throw new OneDriveException("這個帳號沒有可用的 OneDrive。");
        return ToItem(document.RootElement);
    }

    public async Task<DriveItemInfo?> GetItemAsync(string accessToken, string itemId, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(accessToken, $"/me/drive/items/{Uri.EscapeDataString(itemId)}", cancellationToken).ConfigureAwait(false);
        return document is null ? null : ToItem(document.RootElement);
    }

    public async Task<DriveItemInfo> EnsureFolderAsync(string accessToken, string parentId, string name, CancellationToken cancellationToken)
    {
        // 先查同名項目；不存在才建立（conflictBehavior=fail，並行建立時 409 再查一次）。
        var existing = await GetChildByNameAsync(accessToken, parentId, name, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            using var response = await SendAsync(
                accessToken,
                () => new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/me/drive/items/{Uri.EscapeDataString(parentId)}/children")
                {
                    Content = JsonContent.Create(new Dictionary<string, object>
                    {
                        ["name"] = name,
                        ["folder"] = new { },
                        ["@microsoft.graph.conflictBehavior"] = "fail",
                    }),
                },
                cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                using var created = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                return ToItem(created.RootElement);
            }

            if (response.StatusCode != HttpStatusCode.Conflict)
            {
                throw await FailureAsync(response, "建立 OneDrive 資料夾失敗。", cancellationToken).ConfigureAwait(false);
            }

            existing = await GetChildByNameAsync(accessToken, parentId, name, cancellationToken).ConfigureAwait(false);
        }

        return existing switch
        {
            { IsFolder: true } folder => folder,
            null => throw new OneDriveException("建立 OneDrive 資料夾失敗。"),
            _ => throw new OneDriveException($"OneDrive 上已經有名為「{name}」的檔案，請改用其他資料夾名稱。"),
        };
    }

    public async Task<IReadOnlyList<DriveItemInfo>> ListChildrenAsync(string accessToken, string folderId, CancellationToken cancellationToken)
    {
        var items = new List<DriveItemInfo>();
        string? next = $"{_baseUrl}/me/drive/items/{Uri.EscapeDataString(folderId)}/children?$top={PageSize}";
        while (next is not null)
        {
            var url = next;
            using var response = await SendAsync(accessToken, () => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw await FailureAsync(response, "讀取 OneDrive 資料夾失敗。", cancellationToken).ConfigureAwait(false);
            }

            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            foreach (var item in document.RootElement.GetProperty("value").EnumerateArray())
            {
                items.Add(ToItem(item));
            }

            // nextLink 是 Graph 給的完整網址；只接受同一個 Graph 位址，避免把 token 送到別處。
            next = document.RootElement.TryGetProperty("@odata.nextLink", out var link) && link.GetString() is { } value
                && value.StartsWith(_baseUrl + "/", StringComparison.OrdinalIgnoreCase)
                    ? value
                    : null;
        }

        return items;
    }

    public Task<DriveItemInfo?> GetChildAsync(string accessToken, string parentId, string name, CancellationToken cancellationToken) =>
        GetChildByNameAsync(accessToken, parentId, name, cancellationToken);

    public async Task<Stream> OpenDownloadAsync(string accessToken, string itemId, CancellationToken cancellationToken)
    {
        // Graph 回 302 導向預先授權的下載網址；HttpClient 跟隨導向時不會帶上 Authorization。
        var response = await SendAsync(
            accessToken,
            () => new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/me/drive/items/{Uri.EscapeDataString(itemId)}/content"),
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            using (response)
            {
                throw await FailureAsync(response, "下載 OneDrive 檔案失敗。", cancellationToken).ConfigureAwait(false);
            }
        }

        return new ResponseStream(response, await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task<OneDriveUploadResult> UploadAsync(
        string accessToken,
        string parentId,
        string name,
        Stream content,
        long size,
        string? ifMatch,
        OneDriveConflictBehavior conflictBehavior,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var behavior = conflictBehavior.ToString().ToLowerInvariant();
        var target = $"{_baseUrl}/me/drive/items/{Uri.EscapeDataString(parentId)}:/{Uri.EscapeDataString(name)}:";
        if (size <= SimpleUploadLimit)
        {
            // 先讀進記憶體：429 重試時要能重送同一份內容。
            var buffer = new byte[size];
            await content.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
            using var response = await SendAsync(
                accessToken,
                () => WithIfMatch(new HttpRequestMessage(HttpMethod.Put, $"{target}/content?@microsoft.graph.conflictBehavior={behavior}") { Content = new ByteArrayContent(buffer) }, ifMatch),
                cancellationToken).ConfigureAwait(false);
            return await UploadResultAsync(response, cancellationToken).ConfigureAwait(false);
        }

        string uploadUrl;
        using (var session = await SendAsync(
            accessToken,
            () => WithIfMatch(new HttpRequestMessage(HttpMethod.Post, $"{target}/createUploadSession")
            {
                Content = JsonContent.Create(new { item = new Dictionary<string, string> { ["@microsoft.graph.conflictBehavior"] = behavior } }),
            }, ifMatch),
            cancellationToken).ConfigureAwait(false))
        {
            if (session.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
            {
                return OneDriveUploadResult.Conflicted;
            }

            if (!session.IsSuccessStatusCode)
            {
                throw await FailureAsync(session, "上傳 OneDrive 檔案失敗。", cancellationToken).ConfigureAwait(false);
            }

            using var document = await JsonDocument.ParseAsync(await session.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            uploadUrl = document.RootElement.GetProperty("uploadUrl").GetString()!;
        }

        var chunk = new byte[UploadChunkSize];
        for (long offset = 0; offset < size;)
        {
            var length = (int)Math.Min(UploadChunkSize, size - offset);
            await content.ReadExactlyAsync(chunk.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
            var start = offset;
            // uploadUrl 是預先授權的網址，Graph 文件要求不要帶 Authorization。
            using var response = await SendAsync(
                accessToken: null,
                () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Put, uploadUrl) { Content = new ByteArrayContent(chunk, 0, length) };
                    request.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, start + length - 1, size);
                    return request;
                },
                cancellationToken).ConfigureAwait(false);
            offset += length;
            if (response.StatusCode == HttpStatusCode.Accepted && offset < size)
            {
                continue;
            }

            return await UploadResultAsync(response, cancellationToken).ConfigureAwait(false);
        }

        throw new OneDriveException("上傳 OneDrive 檔案失敗。");
    }

    private async Task<OneDriveUploadResult> UploadResultAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
        {
            return OneDriveUploadResult.Conflicted;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await FailureAsync(response, "上傳 OneDrive 檔案失敗。", cancellationToken).ConfigureAwait(false);
        }

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return new OneDriveUploadResult(ToItem(document.RootElement), false);
    }

    private static HttpRequestMessage WithIfMatch(HttpRequestMessage request, string? ifMatch)
    {
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return request;
    }

    private async Task<DriveItemInfo?> GetChildByNameAsync(string accessToken, string parentId, string name, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(accessToken, $"/me/drive/items/{Uri.EscapeDataString(parentId)}:/{Uri.EscapeDataString(name)}", cancellationToken)
            .ConfigureAwait(false);
        return document is null ? null : ToItem(document.RootElement);
    }

    /// <summary>GET JSON；404 回傳 null。</summary>
    private async Task<JsonDocument?> GetJsonAsync(string accessToken, string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(accessToken, () => new HttpRequestMessage(HttpMethod.Get, _baseUrl + path), cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await FailureAsync(response, "讀取 OneDrive 失敗。", cancellationToken).ConfigureAwait(false);
        }

        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>送出請求；429 / 503 依 Retry-After 重試。每次重試重新建立 request（HttpRequestMessage 不能重送）。</summary>
    internal async Task<HttpResponseMessage> SendAsync(string? accessToken, Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = createRequest();
            if (accessToken is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new OneDriveException("暫時無法連線到 OneDrive，請稍後再試。", ex);
            }

            if (response.StatusCode is not (HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable) || attempt >= MaxRetries)
            {
                return response;
            }

            var delay = RetryDelay(response);
            LogThrottled(logger, (int)response.StatusCode, delay.TotalSeconds);
            response.Dispose();
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static TimeSpan RetryDelay(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        var delay = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(2));
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delay;
    }

    private async Task<OneDriveException> FailureAsync(HttpResponseMessage response, string summary, CancellationToken cancellationToken)
    {
        string? code = null;
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            code = document.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("code", out var c) ? c.GetString() : null;
        }
        catch (JsonException)
        {
            // 非 JSON 的錯誤回應：只記錄 status。
        }

        LogGraphError(logger, (int)response.StatusCode, code);
        return response.StatusCode == HttpStatusCode.Unauthorized
            ? new OneDriveAuthorizationException("OneDrive 授權已失效，請重新連結。")
            : response.StatusCode == HttpStatusCode.TooManyRequests
                ? new OneDriveException("OneDrive 暫時限制了存取次數，請稍後再試。")
                : new OneDriveException(summary);
    }

    internal static DriveItemInfo ToItem(JsonElement json) => new(
        json.GetProperty("id").GetString()!,
        json.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
        json.TryGetProperty("folder", out _),
        json.TryGetProperty("eTag", out var etag) ? etag.GetString() : null,
        json.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : 0,
        json.TryGetProperty("lastModifiedDateTime", out var modified) && modified.GetString() is { } text
            ? DateTimeOffset.Parse(text, CultureInfo.InvariantCulture)
            : DateTimeOffset.MinValue);

    [LoggerMessage(Level = LogLevel.Information, Message = "Microsoft Graph returned {StatusCode}; retrying after {Seconds} seconds")]
    private static partial void LogThrottled(ILogger logger, int statusCode, double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Microsoft Graph returned {StatusCode} ({Code})")]
    private static partial void LogGraphError(ILogger logger, int statusCode, string? code);
}

/// <summary>下載串流：Dispose 時一併釋放 HTTP 回應。</summary>
internal sealed class ResponseStream(HttpResponseMessage response, Stream inner) : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.ReadAsync(buffer, offset, count, cancellationToken);

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
            response.Dispose();
        }

        base.Dispose(disposing);
    }
}
