using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ymir.Platform.Auditing;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Application.Files;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Application.Plugins;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Connectors.OneDrive;

/// <summary>某次執行中呼叫 OneDrive 工具的對象：使用者與（由 execution 推導的）工作目錄。</summary>
public sealed record OneDriveToolContext(Guid UserId, Guid ExecutionId, string WorkingDirectory);

/// <param name="Text">給 Agent 的結果（JSON 或摘要）；不含 token、eTag、預授權網址或 host 路徑。</param>
public sealed record OneDriveToolResult(string Text, bool IsError)
{
    public static OneDriveToolResult Error(string message) => new(message, true);
}

/// <summary>MCP <c>tools/list</c> 的一項。</summary>
public sealed record OneDriveToolDefinition(string Name, string Description, JsonObject InputSchema);

/// <summary>
/// OneDrive 外掛的 Agent 工具（ADR-0030 §4）。只在後端以使用者自己的 access token 呼叫 Graph；Agent 拿不到任何 Microsoft 憑證。
/// 每個 item id 都重新查詢、必須屬於使用者自己的 drive；工作目錄的檔案只經 <see cref="IWorkspaceFileReader"/> /
/// <see cref="IWorkspaceFileWriter"/> 在 runtime 內讀寫。寫入類工具每次寫稽核（target 為 Graph item id，不記檔名）。
/// </summary>
public sealed partial class OneDriveAgentTools(
    IVibeMakerDbContext db,
    IUserDirectory users,
    PluginService plugins,
    OneDriveConnectionService connections,
    OneDriveBrowser browser,
    IOneDriveClient graph,
    IWorkspaceFileReader reader,
    IWorkspaceFileWriter writer,
    IAuditLog auditLog,
    TimeProvider timeProvider,
    ILogger<OneDriveAgentTools> logger)
{
    /// <summary>單檔上限，與同步資料夾相同（ADR-0022 決定 6、ADR-0030 §4）。</summary>
    public const long MaxFileBytes = 100L * 1024 * 1024;

    // 結果是給模型讀的 MCP 文字內容（不會嵌進 HTML）：中文不跳脫成 \uXXXX，省 token 也比較好讀。
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static IReadOnlyList<OneDriveToolDefinition> Definitions { get; } =
    [
        new("search", "以名稱搜尋使用者自己的 OneDrive（最多 50 筆），回傳每個項目的 id、名稱、類型、大小與修改時間。",
            Schema(("query", "string", "要搜尋的名稱關鍵字", true))),
        new("list_folder", "列出 OneDrive 資料夾的內容。省略 folderId 時列出根目錄。",
            Schema(("folderId", "string", "資料夾的 item id（由 search 或 list_folder 取得）", false))),
        new("download", $"把 OneDrive 上的一個檔案下載到工作目錄的 {OneDrivePaths.AgentDirectory}/ 底下，回傳相對路徑。只能下載檔案（不能下載資料夾），單檔最大 100 MB；同名的本機檔案會被覆蓋。",
            Schema(("itemId", "string", "檔案的 item id", true))),
        new("upload", "把工作目錄裡的檔案上傳到 OneDrive。同名檔案預設不覆蓋；只有使用者確實要求取代時才設定 overwrite=true。",
            Schema(
                ("path", "string", "工作目錄內的相對路徑，例如 report.docx 或 out/report.docx", true),
                ("folderId", "string", "目標資料夾的 item id；省略時為根目錄", false),
                ("name", "string", "OneDrive 上的檔名；省略時沿用本機檔名", false),
                ("overwrite", "boolean", "同名檔案已存在時是否覆蓋（預設 false）", false))),
        new("create_folder", "在 OneDrive 建立資料夾；同名資料夾已存在時回傳既有的資料夾。",
            Schema(("name", "string", "資料夾名稱", true), ("parentId", "string", "上層資料夾的 item id；省略時為根目錄", false))),
        new("move", "移動及 / 或改名 OneDrive 上的項目。目標位置已有同名項目時不會覆蓋。",
            Schema(
                ("itemId", "string", "要移動或改名的項目 id", true),
                ("newParentId", "string", "新的上層資料夾 id；省略時不移動", false),
                ("newName", "string", "新名稱；省略時不改名", false))),
        new("delete", "刪除 OneDrive 上的檔案或資料夾（放進使用者的 OneDrive 回收筒，可以還原）。只在使用者明確要求刪除時使用。",
            Schema(("itemId", "string", "要刪除的項目 id", true))),
    ];

    /// <summary>
    /// 驗證這次呼叫（ADR-0030 §2）：execution 必須執行中、屬於 token 的使用者、不是群組執行；
    /// 帳號有效、管理員允許、使用者啟用外掛且 OneDrive 已連結。任何一項不成立回傳 null（呼叫端回傳摘要，不區分原因）。
    /// </summary>
    public async Task<OneDriveToolContext?> ResolveAsync(Guid userId, Guid executionId, CancellationToken cancellationToken)
    {
        var run = await (
            from execution in db.AgentExecutions.AsNoTracking()
            join conversation in db.Conversations.AsNoTracking() on execution.ConversationId equals conversation.Id
            where execution.Id == executionId
            select new { execution.UserId, execution.Status, ConversationId = conversation.Id, ConversationOwner = conversation.UserId, conversation.ProjectId })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        // 群組執行的 conversation 擁有者是群組 id（ADR-0026），workspace 是成員共用的：不提供個人的 OneDrive。
        if (run is null || run.UserId != userId || run.ConversationOwner != userId || run.Status != ExecutionStatus.Running)
        {
            return null;
        }

        var user = await users.FindAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null || user.Status != UserStatus.Active || !await plugins.IsOneDriveAgentEnabledAsync(userId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new OneDriveToolContext(userId, executionId, RuntimePaths.WorkingDirectoryFor(run.ConversationId, run.ProjectId));
    }

    public async Task<OneDriveToolResult> CallAsync(OneDriveToolContext context, string tool, JsonElement arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            return tool switch
            {
                "search" => await SearchAsync(context, arguments, cancellationToken).ConfigureAwait(false),
                "list_folder" => await ListFolderAsync(context, arguments, cancellationToken).ConfigureAwait(false),
                "download" => await DownloadAsync(context, arguments, cancellationToken).ConfigureAwait(false),
                "upload" => await UploadAsync(context, arguments, cancellationToken).ConfigureAwait(false),
                "create_folder" => await CreateFolderAsync(context, arguments, cancellationToken).ConfigureAwait(false),
                "move" => await MoveAsync(context, arguments, cancellationToken).ConfigureAwait(false),
                "delete" => await DeleteAsync(context, arguments, cancellationToken).ConfigureAwait(false),
                _ => OneDriveToolResult.Error($"沒有名為 {tool} 的 OneDrive 工具。"),
            };
        }
        catch (OneDriveException exception)
        {
            // OneDriveException 的訊息是摘要（不含 token 或 Graph 原始回應），可以給 Agent。
            return OneDriveToolResult.Error(exception.Message);
        }
    }

    private async Task<OneDriveToolResult> SearchAsync(OneDriveToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var items = await browser.SearchAsync(context.UserId, String(arguments, "query"), cancellationToken).ConfigureAwait(false);
        return Json(new { items = items.Select(ToJson) });
    }

    private async Task<OneDriveToolResult> ListFolderAsync(OneDriveToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var page = await browser.BrowseAsync(context.UserId, String(arguments, "folderId"), cancellationToken).ConfigureAwait(false);
        return Json(new { folderId = page.FolderId, parentId = page.ParentItemId, items = page.Items.Select(ToJson) });
    }

    private async Task<OneDriveToolResult> DownloadAsync(OneDriveToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var drive = await DriveAsync(context, cancellationToken).ConfigureAwait(false);
        var item = await OwnItemAsync(drive, String(arguments, "itemId"), cancellationToken).ConfigureAwait(false);
        if (item.IsFolder)
        {
            return OneDriveToolResult.Error("這是資料夾；請用 list_folder 列出內容後逐一下載檔案。");
        }

        if (item.Size > MaxFileBytes)
        {
            return OneDriveToolResult.Error("檔案超過 100 MB，無法下載。");
        }

        var path = $"{OneDrivePaths.AgentDirectory}/{LocalFileName(item.Name)}";
        await using var content = await graph.OpenDownloadAsync(drive.Token, item.Id, cancellationToken).ConfigureAwait(false);
        if (!await writer.WriteAsync(context.UserId, context.WorkingDirectory, path, content, item.Size, cancellationToken).ConfigureAwait(false))
        {
            return OneDriveToolResult.Error("無法寫入工作目錄，請稍後再試。");
        }

        return Json(new { path, size = item.Size });
    }

    private async Task<OneDriveToolResult> UploadAsync(OneDriveToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var path = String(arguments, "path")?.Trim();
        if (path is null || !WorkspacePathRules.IsSafeRelativePath(path))
        {
            return OneDriveToolResult.Error("path 必須是工作目錄內的相對路徑（不能以 / 開頭、不能包含 ..，也不能是隱藏檔）。");
        }

        var name = String(arguments, "name")?.Trim() ?? WorkspacePathRules.FileName(path);
        if (!OneDrivePaths.IsValidName(name))
        {
            return OneDriveToolResult.Error("OneDrive 檔名不合法（不能包含 \" * : < > ? \\ | 或控制字元）。");
        }

        var overwrite = arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("overwrite", out var o) && o.ValueKind == JsonValueKind.True;
        var drive = await DriveAsync(context, cancellationToken).ConfigureAwait(false);
        var folder = await FolderAsync(drive, String(arguments, "folderId"), cancellationToken).ConfigureAwait(false);
        OneDriveToolResult? result = null;
        await reader.ReadAsync(context.UserId, context.WorkingDirectory, [path], async (file, ct) =>
        {
            if (file.Size > MaxFileBytes)
            {
                result = OneDriveToolResult.Error("檔案超過 100 MB，無法上傳。");
                return;
            }

            var upload = await graph.UploadAsync(drive.Token, folder.Id, name, file.Content, file.Size, null,
                overwrite ? OneDriveConflictBehavior.Replace : OneDriveConflictBehavior.Fail, ct).ConfigureAwait(false);
            if (upload.Item is not { } uploaded)
            {
                result = OneDriveToolResult.Error("OneDrive 上已經有同名的檔案；使用者確實要取代時才設定 overwrite=true，或改用其他名稱。");
                return;
            }

            await AuditAsync(context, "upload", uploaded.Id, ct).ConfigureAwait(false);
            result = Json(new { uploaded = ToJson(uploaded) });
        }, cancellationToken).ConfigureAwait(false);
        return result ?? OneDriveToolResult.Error("工作目錄裡找不到這個檔案。");
    }

    private async Task<OneDriveToolResult> CreateFolderAsync(OneDriveToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var name = String(arguments, "name")?.Trim();
        if (name is null || !OneDrivePaths.IsValidName(name))
        {
            return OneDriveToolResult.Error("資料夾名稱不合法（不能包含 \" * : < > ? \\ | 或控制字元）。");
        }

        var drive = await DriveAsync(context, cancellationToken).ConfigureAwait(false);
        var parent = await FolderAsync(drive, String(arguments, "parentId"), cancellationToken).ConfigureAwait(false);
        var folder = await graph.EnsureFolderAsync(drive.Token, parent.Id, name, cancellationToken).ConfigureAwait(false);
        await AuditAsync(context, "create_folder", folder.Id, cancellationToken).ConfigureAwait(false);
        return Json(new { folder = ToJson(folder) });
    }

    private async Task<OneDriveToolResult> MoveAsync(OneDriveToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var newParentId = String(arguments, "newParentId");
        var newName = String(arguments, "newName")?.Trim();
        if (newParentId is null && newName is null)
        {
            return OneDriveToolResult.Error("請指定 newParentId 或 newName。");
        }

        if (newName is not null && !OneDrivePaths.IsValidName(newName))
        {
            return OneDriveToolResult.Error("新名稱不合法（不能包含 \" * : < > ? \\ | 或控制字元）。");
        }

        var drive = await DriveAsync(context, cancellationToken).ConfigureAwait(false);
        var item = await OwnItemAsync(drive, String(arguments, "itemId"), cancellationToken).ConfigureAwait(false);
        if (item.Id == drive.RootId)
        {
            return OneDriveToolResult.Error("不能移動 OneDrive 的根目錄。");
        }

        var parent = newParentId is null ? null : await FolderAsync(drive, newParentId, cancellationToken).ConfigureAwait(false);
        var moved = await graph.MoveAsync(drive.Token, item.Id, parent?.Id, newName, cancellationToken).ConfigureAwait(false)
            ?? throw new OneDriveItemInvalidException();
        await AuditAsync(context, "move", item.Id, cancellationToken).ConfigureAwait(false);
        return Json(new { item = ToJson(moved) });
    }

    private async Task<OneDriveToolResult> DeleteAsync(OneDriveToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var drive = await DriveAsync(context, cancellationToken).ConfigureAwait(false);
        var item = await OwnItemAsync(drive, String(arguments, "itemId"), cancellationToken).ConfigureAwait(false);
        if (item.Id == drive.RootId)
        {
            return OneDriveToolResult.Error("不能刪除 OneDrive 的根目錄。");
        }

        if (!await graph.DeleteAsync(drive.Token, item.Id, cancellationToken).ConfigureAwait(false))
        {
            throw new OneDriveItemInvalidException();
        }

        await AuditAsync(context, "delete", item.Id, cancellationToken).ConfigureAwait(false);
        return Json(new { deleted = item.Id, name = item.Name, note = "已放進 OneDrive 回收筒，使用者可以從回收筒還原。" });
    }

    private sealed record DriveContext(string Token, string DriveId, string RootId);

    private async Task<DriveContext> DriveAsync(OneDriveToolContext context, CancellationToken cancellationToken)
    {
        var token = await connections.GetAccessTokenAsync(context.UserId, cancellationToken).ConfigureAwait(false);
        var driveId = await graph.GetDriveIdAsync(token, cancellationToken).ConfigureAwait(false);
        var root = await graph.GetRootAsync(token, cancellationToken).ConfigureAwait(false);
        return new DriveContext(token, driveId, root.Id);
    }

    /// <summary>Agent 傳入的 item id 一律重新查詢，必須屬於使用者自己的 drive（拒絕「與我共用」、SharePoint、remoteItem）。</summary>
    private async Task<DriveItemInfo> OwnItemAsync(DriveContext drive, string? itemId, CancellationToken cancellationToken)
    {
        OneDriveBrowser.ValidateItemId(itemId);
        if (itemId is null)
        {
            throw new OneDriveItemInvalidException();
        }

        var item = await graph.GetItemAsync(drive.Token, itemId, cancellationToken).ConfigureAwait(false);
        return item is not null && (item.Id == drive.RootId || OneDriveBrowser.BelongsToDrive(item, drive.DriveId))
            ? item
            : throw new OneDriveItemInvalidException();
    }

    private async Task<DriveItemInfo> FolderAsync(DriveContext drive, string? folderId, CancellationToken cancellationToken)
    {
        var folder = folderId is null
            ? await graph.GetRootAsync(drive.Token, cancellationToken).ConfigureAwait(false)
            : await OwnItemAsync(drive, folderId, cancellationToken).ConfigureAwait(false);
        return folder.IsFolder ? folder : throw new OneDriveException("指定的 id 不是資料夾。");
    }

    private Task AuditAsync(OneDriveToolContext context, string action, string itemId, CancellationToken cancellationToken)
    {
        LogAgentWrite(logger, action, context.ExecutionId);
        return auditLog.WriteAsync(
            new AuditEntry(AuditActor.ForUser(context.UserId), $"onedrive.agent.{action}", "onedrive-item", itemId.Length > 100 ? itemId[..100] : itemId,
                AuditResult.Success, timeProvider.GetUtcNow(), null),
            cancellationToken);
    }

    /// <summary>
    /// 下載到工作目錄時的檔名：只取一段、去掉路徑分隔與控制字元、不能是隱藏檔（<see cref="WorkspacePathRules"/>）。
    /// </summary>
    public static string LocalFileName(string name)
    {
        var cleaned = new string([.. (name ?? string.Empty).Select(c => c is '/' or '\\' || char.IsControl(c) ? '_' : c)]).Trim().TrimStart('.');
        if (cleaned.Length > 200)
        {
            cleaned = cleaned[..200];
        }

        return cleaned.Length == 0 || cleaned is "." or ".." || WorkspacePathRules.IsHidden(cleaned) ? "未命名檔案" : cleaned;
    }

    private static object ToJson(OneDriveBrowserItem item) =>
        new { id = item.Id, name = item.Name, type = item.IsFolder ? "folder" : "file", size = item.Size, modified = item.LastModified };

    private static object ToJson(DriveItemInfo item) =>
        new { id = item.Id, name = item.Name, type = item.IsFolder ? "folder" : "file", size = item.Size, modified = item.LastModified };

    private static OneDriveToolResult Json(object value) => new(JsonSerializer.Serialize(value, s_json), false);

    private static string? String(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() is { Length: > 0 } text ? text : null
            : null;

    private static JsonObject Schema(params (string Name, string Type, string Description, bool Required)[] properties) => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(properties.Select(p => KeyValuePair.Create<string, JsonNode?>(p.Name, new JsonObject
        {
            ["type"] = p.Type,
            ["description"] = p.Description,
        }))),
        ["required"] = new JsonArray([.. properties.Where(p => p.Required).Select(p => (JsonNode?)JsonValue.Create(p.Name))]),
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "OneDrive agent tool {Action} for execution {ExecutionId}")]
    private static partial void LogAgentWrite(ILogger logger, string action, Guid executionId);
}
