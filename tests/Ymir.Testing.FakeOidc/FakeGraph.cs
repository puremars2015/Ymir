using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Ymir.Testing.FakeOidc;

/// <summary>
/// 模擬 Microsoft Graph 的 OneDrive（ADR-0013）：每個帳號一個記憶體中的 drive。沙箱與 CI 連不到 graph.microsoft.com，
/// 用它驗證 Ymir 的 OneDrive connector。只實作 Ymir 用到的端點，行為（eTag、If-Match 412、conflictBehavior、
/// upload session、429 + Retry-After）比照 Graph 文件。路徑前綴 <c>/graph/v1.0</c>。
/// </summary>
public sealed class FakeGraphStore
{
    private readonly ConcurrentDictionary<string, FakeDrive> _drives = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, UploadSession> _sessions = new();
    private int _throttle;
    private int _sessionsCreated;

    /// <summary>測試用：接下來的 <paramref name="count"/> 個 Graph 請求回 429（Retry-After: 1）。</summary>
    public void ThrottleNext(int count) => Interlocked.Exchange(ref _throttle, count);

    internal bool ConsumeThrottle()
    {
        while (true)
        {
            var current = Volatile.Read(ref _throttle);
            if (current <= 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _throttle, current - 1, current) == current)
            {
                return true;
            }
        }
    }

    public FakeDrive DriveOf(string account) => _drives.GetOrAdd(account.ToLowerInvariant(), a => new FakeDrive(a));

    internal string CreateSession(FakeDrive drive, string parentId, string name, string? ifMatch, string conflictBehavior)
    {
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _sessions[id] = new UploadSession(drive, parentId, name, ifMatch, conflictBehavior, new MemoryStream());
        return id;
    }

    /// <summary>測試用：目前進行中的 upload session 數（大檔案是否走 upload session）。</summary>
    public int UploadSessionsCreated => Volatile.Read(ref _sessionsCreated);

    internal void CountSession() => Interlocked.Increment(ref _sessionsCreated);

    internal UploadSession? Session(string id) => _sessions.TryGetValue(id, out var session) ? session : null;

    internal void EndSession(string id) => _sessions.TryRemove(id, out _);

    internal sealed record UploadSession(FakeDrive Drive, string ParentId, string Name, string? IfMatch, string ConflictBehavior, MemoryStream Buffer);
}

/// <summary>一個帳號的 drive；測試可直接讀寫檔案（模擬使用者在 OneDrive 網頁上的操作）。</summary>
public sealed class FakeDrive
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, FakeItem> _items = [];

    internal FakeDrive(string account)
    {
        Account = account;
        DriveId = "b!" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("drive:" + account)))[..24];
        RootId = NewId();
        _items[RootId] = new FakeItem(RootId, "root", null, IsFolder: true, [], 1, DateTimeOffset.UtcNow);
    }

    public string Account { get; }

    public string DriveId { get; }

    public string RootId { get; }

    /// <summary>以 <c>/a/b/c.txt</c> 形式的路徑讀取檔案內容；不存在時為 null。</summary>
    public byte[]? ReadFile(string path)
    {
        lock (_lock)
        {
            return FindByPathLocked(path) is { IsFolder: false } item ? item.Content : null;
        }
    }

    public bool FolderExists(string path)
    {
        lock (_lock)
        {
            return FindByPathLocked(path) is { IsFolder: true };
        }
    }

    /// <summary>列出路徑底下（遞迴）所有檔案的相對路徑。</summary>
    public IReadOnlyList<string> ListFiles(string folderPath)
    {
        lock (_lock)
        {
            if (FindByPathLocked(folderPath) is not { IsFolder: true } folder)
            {
                return [];
            }

            var result = new List<string>();
            void Walk(FakeItem parent, string prefix)
            {
                foreach (var child in _items.Values.Where(i => i.ParentId == parent.Id).OrderBy(i => i.Name, StringComparer.Ordinal))
                {
                    var path = prefix.Length == 0 ? child.Name : $"{prefix}/{child.Name}";
                    if (child.IsFolder)
                    {
                        Walk(child, path);
                    }
                    else
                    {
                        result.Add(path);
                    }
                }
            }

            Walk(folder, string.Empty);
            return result;
        }
    }

    /// <summary>測試用：在路徑寫入檔案（自動建立資料夾），版本號遞增、eTag 改變。</summary>
    public void WriteFile(string path, byte[] content)
    {
        lock (_lock)
        {
            var segments = Segments(path);
            var parent = _items[RootId];
            foreach (var segment in segments[..^1])
            {
                parent = ChildLocked(parent.Id, segment) ?? AddLocked(parent.Id, segment, isFolder: true, []);
            }

            UpsertFileLocked(parent.Id, segments[^1], content);
        }
    }

    internal FakeItem? Get(string id)
    {
        lock (_lock)
        {
            return _items.GetValueOrDefault(id);
        }
    }

    internal FakeItem? FindByPath(string path)
    {
        lock (_lock)
        {
            return FindByPathLocked(path);
        }
    }

    internal IReadOnlyList<FakeItem> Children(string id)
    {
        lock (_lock)
        {
            return [.. _items.Values.Where(i => i.ParentId == id).OrderBy(i => i.Name, StringComparer.Ordinal)];
        }
    }

    internal FakeItem? Child(string parentId, string name)
    {
        lock (_lock)
        {
            return ChildLocked(parentId, name);
        }
    }

    /// <summary>建立資料夾；同名已存在時依 conflictBehavior 回傳既有項目（replace/fail 由呼叫端決定）。</summary>
    internal (FakeItem? Item, bool Conflict) CreateFolder(string parentId, string name, string conflictBehavior)
    {
        lock (_lock)
        {
            if (!_items.TryGetValue(parentId, out var parent) || !parent.IsFolder)
            {
                return (null, false);
            }

            if (ChildLocked(parentId, name) is { } existing)
            {
                return conflictBehavior == "fail" ? (existing, true) : (existing, false);
            }

            return (AddLocked(parentId, name, isFolder: true, []), false);
        }
    }

    /// <summary>
    /// 上傳檔案（比照 Graph）：<paramref name="ifMatch"/> 與目前 eTag 不符時 412；
    /// conflictBehavior=fail 且同名已存在時 409；rename 時另取 <c>名稱 1.ext</c>；replace（預設）覆蓋。
    /// </summary>
    internal (FakeItem? Item, int Status) Upload(string parentId, string name, byte[] content, string? ifMatch, string conflictBehavior)
    {
        lock (_lock)
        {
            if (!_items.TryGetValue(parentId, out var parent) || !parent.IsFolder)
            {
                return (null, StatusCodes.Status404NotFound);
            }

            var existing = ChildLocked(parentId, name);
            if (ifMatch is not null && ifMatch != "*" && (existing is null || existing.ETag != ifMatch))
            {
                return (null, StatusCodes.Status412PreconditionFailed);
            }

            if (existing is not null && ifMatch is null)
            {
                if (conflictBehavior == "fail" || existing.IsFolder)
                {
                    return (null, StatusCodes.Status409Conflict);
                }

                if (conflictBehavior == "rename")
                {
                    var dot = name.LastIndexOf('.');
                    var (stem, extension) = dot > 0 ? (name[..dot], name[dot..]) : (name, string.Empty);
                    var n = 1;
                    while (ChildLocked(parentId, $"{stem} {n}{extension}") is not null)
                    {
                        n++;
                    }

                    name = $"{stem} {n}{extension}";
                }
            }

            return (UpsertFileLocked(parentId, name, content), StatusCodes.Status201Created);
        }
    }

    private FakeItem UpsertFileLocked(string parentId, string name, byte[] content)
    {
        if (ChildLocked(parentId, name) is { } existing)
        {
            var updated = existing with { Content = content, Version = existing.Version + 1, Modified = DateTimeOffset.UtcNow };
            _items[existing.Id] = updated;
            return updated;
        }

        return AddLocked(parentId, name, isFolder: false, content);
    }

    private FakeItem AddLocked(string parentId, string name, bool isFolder, byte[] content)
    {
        var item = new FakeItem(NewId(), name, parentId, isFolder, content, 1, DateTimeOffset.UtcNow);
        _items[item.Id] = item;
        return item;
    }

    private FakeItem? ChildLocked(string parentId, string name) =>
        _items.Values.FirstOrDefault(i => i.ParentId == parentId && string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));

    private FakeItem? FindByPathLocked(string path)
    {
        var current = _items[RootId];
        foreach (var segment in Segments(path))
        {
            if (ChildLocked(current.Id, segment) is not { } next)
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    internal string PathOf(FakeItem item)
    {
        lock (_lock)
        {
            var names = new List<string>();
            for (var current = item; current.ParentId is not null; current = _items[current.ParentId])
            {
                names.Insert(0, current.Name);
            }

            return "/" + string.Join('/', names);
        }
    }

    private static string[] Segments(string path) => path.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static string NewId() => "01" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
}

internal sealed record FakeItem(string Id, string Name, string? ParentId, bool IsFolder, byte[] Content, int Version, DateTimeOffset Modified)
{
    public string ETag => $"\"{{{Id}}},{Version}\"";
}

internal static class FakeGraphEndpoints
{
    private const string Prefix = "/graph/v1.0";

    /// <summary>刻意比 Graph 的 200 小，讓測試走到分頁。</summary>
    private const int ChildrenPageSize = 50;

    public static void MapFakeGraph(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Prefix + "/me", (HttpContext context, FakeOidcIssuer issuer) => Authorized(context, (account, _) =>
        {
            return Results.Json(new
            {
                id = FakeOidcIssuer.StableGuid($"oid:{account}").ToString("D"),
                userPrincipalName = issuer.UserPrincipalName(account),
                displayName = account,
            });
        }));
        endpoints.MapGet(Prefix + "/me/drive", (HttpContext context, FakeGraphStore store) => Authorized(context, (_, drive) =>
            Results.Json(new { id = drive.DriveId, driveType = "business" })));
        endpoints.MapMethods(Prefix + "/me/drive/{**rest}", ["GET", "POST", "PUT"], (HttpContext context, string rest) =>
            Authorized(context, (_, drive) => DriveRequestAsync(context, drive, rest)));
        // upload session 的 uploadUrl 是預先授權的網址，不需要 Authorization（與 Graph 相同）。
        endpoints.MapPut("/graph/upload/{sessionId}", UploadChunkAsync);
    }

    private static async Task<IResult> Authorized(HttpContext context, Func<string, FakeDrive, Task<IResult>> handler)
    {
        var store = context.RequestServices.GetRequiredService<FakeGraphStore>();
        if (store.ConsumeThrottle())
        {
            context.Response.Headers.RetryAfter = "1";
            return Error(StatusCodes.Status429TooManyRequests, "activityLimitReached", "Throttled");
        }

        var header = context.Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.Ordinal) ? header["Bearer ".Length..] : null;
        if (context.RequestServices.GetRequiredService<FakeOidcIssuer>().ResolveAccessToken(token) is not { } account)
        {
            return Error(StatusCodes.Status401Unauthorized, "InvalidAuthenticationToken", "Access token is empty or invalid.");
        }

        return await handler(account, store.DriveOf(account));
    }

    private static Task<IResult> Authorized(HttpContext context, Func<string, FakeDrive, IResult> handler) =>
        Authorized(context, (account, drive) => Task.FromResult(handler(account, drive)));

    private static async Task<IResult> DriveRequestAsync(HttpContext context, FakeDrive drive, string rest)
    {
        var method = context.Request.Method;
        // root、root:/path:、items/{id}、items/{id}/children、items/{id}/content、items/{parent}:/{name}:/content|createUploadSession
        if (rest == "root" && method == "GET")
        {
            return ItemResult(drive, drive.Get(drive.RootId)!);
        }

        if (rest.StartsWith("root:", StringComparison.Ordinal) && method == "GET")
        {
            var path = Uri.UnescapeDataString(rest["root:".Length..].TrimEnd(':'));
            return drive.FindByPath(path) is { } item ? ItemResult(drive, item) : NotFound();
        }

        if (!rest.StartsWith("items/", StringComparison.Ordinal))
        {
            return NotFound();
        }

        var afterItems = rest["items/".Length..];
        var colon = afterItems.IndexOf(":/", StringComparison.Ordinal);
        if (colon > 0)
        {
            var parentId = afterItems[..colon];
            var tail = afterItems[(colon + 2)..];
            var end = tail.LastIndexOf(":/", StringComparison.Ordinal);
            if (end < 0)
            {
                return NotFound();
            }

            var name = Uri.UnescapeDataString(tail[..end]);
            var action = tail[(end + 2)..];
            var ifMatch = context.Request.Headers.IfMatch.ToString() is { Length: > 0 } value ? value : null;
            if (action == "content" && method == "PUT")
            {
                using var buffer = new MemoryStream();
                await context.Request.Body.CopyToAsync(buffer, context.RequestAborted);
                var behavior = context.Request.Query["@microsoft.graph.conflictBehavior"].ToString() is { Length: > 0 } b ? b : "replace";
                return UploadResult(drive, drive.Upload(parentId, name, buffer.ToArray(), ifMatch, behavior));
            }

            if (action == "createUploadSession" && method == "POST")
            {
                if (drive.Get(parentId) is not { IsFolder: true })
                {
                    return NotFound();
                }

                var request = await context.Request.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>(context.RequestAborted) ?? [];
                var behavior = request.TryGetValue("item", out var item) && item.TryGetProperty("@microsoft.graph.conflictBehavior", out var cb) ? cb.GetString() ?? "replace" : "replace";
                var existing = drive.Child(parentId, name);
                // Graph 在建立 session 時就檢查 If-Match 與 conflictBehavior=fail。
                if (ifMatch is not null && ifMatch != "*" && existing?.ETag != ifMatch)
                {
                    return Error(StatusCodes.Status412PreconditionFailed, "preconditionFailed", "ETag does not match.");
                }

                if (existing is not null && ifMatch is null && behavior == "fail")
                {
                    return Error(StatusCodes.Status409Conflict, "nameAlreadyExists", "Name already exists.");
                }

                var store = context.RequestServices.GetRequiredService<FakeGraphStore>();
                store.CountSession();
                var sessionId = store.CreateSession(drive, parentId, name, ifMatch, behavior);
                var root = $"{context.Request.Scheme}://{context.Request.Host}";
                return Results.Json(new { uploadUrl = $"{root}/graph/upload/{sessionId}", expirationDateTime = DateTimeOffset.UtcNow.AddHours(1) });
            }

            return NotFound();
        }

        var parts = afterItems.Split('/');
        var target = drive.Get(parts[0]);
        if (target is null)
        {
            return NotFound();
        }

        switch (parts.Length == 1 ? string.Empty : parts[1])
        {
            case "" when method == "GET":
                return ItemResult(drive, target);
            case "children" when method == "GET":
                {
                    // 分頁比照 Graph：$top 決定每頁筆數，下一頁以 @odata.nextLink（含 $skiptoken）提供。
                    var children = drive.Children(target.Id);
                    var top = int.TryParse(context.Request.Query["$top"], CultureInfo.InvariantCulture, out var t) && t > 0 ? Math.Min(t, ChildrenPageSize) : ChildrenPageSize;
                    var skip = int.TryParse(context.Request.Query["$skiptoken"], CultureInfo.InvariantCulture, out var k) && k > 0 ? k : 0;
                    var page = children.Skip(skip).Take(top).Select(i => ItemJson(drive, i)).ToList();
                    var body = new Dictionary<string, object?> { ["value"] = page };
                    if (skip + top < children.Count)
                    {
                        body["@odata.nextLink"] = $"{context.Request.Scheme}://{context.Request.Host}{Prefix}/me/drive/items/{target.Id}/children?$top={top}&$skiptoken={skip + top}";
                    }

                    return Results.Json(body);
                }
            case "children" when method == "POST":
                var folderRequest = await context.Request.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>(context.RequestAborted) ?? [];
                var folderName = folderRequest.TryGetValue("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                var behavior = folderRequest.TryGetValue("@microsoft.graph.conflictBehavior", out var b) ? b.GetString() ?? "fail" : "fail";
                if (folderName.Length == 0 || !folderRequest.ContainsKey("folder"))
                {
                    return Error(StatusCodes.Status400BadRequest, "invalidRequest", "Only folder creation is supported.");
                }

                var (folder, conflict) = drive.CreateFolder(target.Id, folderName, behavior);
                return conflict ? Error(StatusCodes.Status409Conflict, "nameAlreadyExists", "Name already exists.")
                    : folder is null ? NotFound()
                    : ItemResult(drive, folder, StatusCodes.Status201Created);
            case "content" when method == "GET" && !target.IsFolder:
                return Results.Bytes(target.Content, "application/octet-stream");
            default:
                return NotFound();
        }
    }

    private static async Task<IResult> UploadChunkAsync(string sessionId, HttpContext context, FakeGraphStore store)
    {
        if (store.Session(sessionId) is not { } session)
        {
            return NotFound();
        }

        // Content-Range: bytes {start}-{end}/{total}
        var range = context.Request.Headers.ContentRange.ToString();
        var match = System.Text.RegularExpressions.Regex.Match(range, @"^bytes (\d+)-(\d+)/(\d+)$");
        if (!match.Success || long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) != session.Buffer.Length)
        {
            return Error(StatusCodes.Status416RangeNotSatisfiable, "invalidRange", "Unexpected range.");
        }

        await context.Request.Body.CopyToAsync(session.Buffer, context.RequestAborted);
        var total = long.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        if (session.Buffer.Length < total)
        {
            return Results.Json(new { nextExpectedRanges = new[] { $"{session.Buffer.Length}-" } }, statusCode: StatusCodes.Status202Accepted);
        }

        store.EndSession(sessionId);
        return UploadResult(session.Drive, session.Drive.Upload(session.ParentId, session.Name, session.Buffer.ToArray(), session.IfMatch, session.ConflictBehavior));
    }

    private static IResult UploadResult(FakeDrive drive, (FakeItem? Item, int Status) result) => result switch
    {
        ({ } item, _) => ItemResult(drive, item, StatusCodes.Status201Created),
        (_, StatusCodes.Status412PreconditionFailed) => Error(StatusCodes.Status412PreconditionFailed, "preconditionFailed", "ETag does not match."),
        (_, StatusCodes.Status409Conflict) => Error(StatusCodes.Status409Conflict, "nameAlreadyExists", "Name already exists."),
        _ => NotFound(),
    };

    private static IResult ItemResult(FakeDrive drive, FakeItem item, int status = StatusCodes.Status200OK) =>
        Results.Json(ItemJson(drive, item), statusCode: status);

    private static Dictionary<string, object?> ItemJson(FakeDrive drive, FakeItem item)
    {
        var json = new Dictionary<string, object?>
        {
            ["id"] = item.Id,
            ["name"] = item.Name,
            ["eTag"] = item.ETag,
            ["cTag"] = item.ETag.Replace("\"{", "\"c:{", StringComparison.Ordinal),
            ["size"] = item.IsFolder ? 0 : item.Content.Length,
            ["lastModifiedDateTime"] = item.Modified,
            ["parentReference"] = item.ParentId is null ? null : new { id = item.ParentId, driveId = drive.DriveId },
        };
        if (item.IsFolder)
        {
            json["folder"] = new { childCount = drive.Children(item.Id).Count };
        }
        else
        {
            json["file"] = new { mimeType = "application/octet-stream" };
        }

        if (item.ParentId is null)
        {
            json["root"] = new { };
        }

        return json;
    }

    private static IResult NotFound() => Error(StatusCodes.Status404NotFound, "itemNotFound", "The resource could not be found.");

    private static IResult Error(int status, string code, string message) =>
        Results.Json(new { error = new { code, message } }, statusCode: status);
}
