using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.Executions;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Application.Connectors.OneDrive;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Contracts.Executions;
using Ymir.VibeMaker.Domain;
using Ymir.VibeMaker.Infrastructure.Runtime;

namespace Ymir.IntegrationTests.Connectors;

/// <summary>
/// OneDrive 同步（ADR-0013 §4）：Fake OIDC + Fake Graph + Local runtime + Scripted harness。
/// 執行前下載、執行後（背景工作）上傳、兩邊都改時保留兩份、大檔案用 upload session、授權失效時停止並保留本機檔案。
/// </summary>
public class OneDriveSyncTests(OneDriveApiFactory factory) : IClassFixture<OneDriveApiFactory>
{
    private const string Root = "/Ymir";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(HttpClient Client, Guid UserId)> ConnectedUserAsync(string account)
    {
        var client = await factory.LoginWithOidcAsync(account);
        var userId = (await client.GetFromJsonAsync<JsonElement>("/api/me", Ct)).GetProperty("id").GetGuid();
        using (var admin = await factory.LoginAsync($"od-sync-admin-{Guid.NewGuid():N}", UserRole.Admin))
        {
            using var allow = await admin.PutAsJsonAsync(
                $"/api/admin/users/{userId}/extensions",
                new SaveUserExtensionsRequest(ExtensionGrantSetting.Inherit, ExtensionGrantSetting.Inherit, ExtensionGrantSetting.Inherit, ExtensionGrantSetting.Allow),
                JsonDefaults.Options,
                Ct);
            allow.EnsureSuccessStatusCode();
        }

        Assert.Equal("/settings?onedrive=connected", await OneDriveApiFactory.ConnectOneDriveAsync(client, account));
        using var root = await client.PutAsJsonAsync("/api/connectors/onedrive/root", new SetOneDriveRootRequest(Root), Ct);
        root.EnsureSuccessStatusCode();
        return (client, userId);
    }

    private string HostDirectory(Guid userId, Guid conversationId, Guid? projectId = null)
    {
        var directory = UserDirectories.For(factory.WorkspaceRoot, userId).HostPathOf(RuntimePaths.WorkingDirectoryFor(conversationId, projectId));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task<List<string>> RunAsync(HttpClient client, Guid conversationId, string content)
    {
        var (response, sent) = await client.SendMessageAsync(conversationId, content);
        response.EnsureSuccessStatusCode();
        var events = await client.ReadEventsAsync(sent!.EventStreamUrl);
        Assert.Equal(ExecutionEventNames.ExecutionCompleted, events[^1].EventType);
        return [.. events.Select(e => e.Data.ToString())];
    }

    private static Task<ConversationOneDriveResponse?> StatusAsync(HttpClient client, Guid conversationId) =>
        client.GetFromJsonAsync<ConversationOneDriveResponse>($"/api/conversations/{conversationId}/onedrive", JsonDefaults.Options, Ct);

    /// <summary>等背景同步在 <paramref name="after"/> 之後完成（成功或失敗）。</summary>
    private static async Task<ConversationOneDriveResponse> WaitForSyncAsync(HttpClient client, Guid conversationId, DateTimeOffset after)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (true)
        {
            var status = (await StatusAsync(client, conversationId))!;
            if (status.State == OneDriveSyncState.Failed || (status.State == OneDriveSyncState.Synced && status.LastSyncedAt >= after))
            {
                return status;
            }

            await Task.Delay(200, timeout.Token);
        }
    }

    private static async Task<ConversationOneDriveResponse> SyncNowAsync(HttpClient client, Guid conversationId)
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        using var response = await client.PostAsync(new Uri($"/api/conversations/{conversationId}/onedrive/sync", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return await WaitForSyncAsync(client, conversationId, before);
    }

    [Fact]
    public async Task Run_UploadsWorkspaceFiles_AndDownloadsCloudChangesBeforeTheNextRun()
    {
        var (client, userId) = await ConnectedUserAsync("od-sync-amy");
        using var _ = client;
        var conversation = await client.CreateConversationAsync(null, "季報: 草稿");
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var firstRun = await RunAsync(client, conversation.Id, "hello");

        Assert.Contains(firstRun, data => data.Contains("正在從 OneDrive 同步", StringComparison.Ordinal));
        var status = await WaitForSyncAsync(client, conversation.Id, before);
        Assert.Equal(OneDriveSyncState.Synced, status.State);
        Assert.Equal(OneDriveAvailability.Ready, status.Availability);
        // 名稱中 OneDrive 不接受的字元換成 _，加上 id 前 8 碼（ADR-0013 §3）。
        Assert.Equal($"chats/季報_ 草稿-{conversation.Id.ToString("N")[..8]}", status.FolderPath);

        var directory = HostDirectory(userId, conversation.Id);
        await File.WriteAllTextAsync(Path.Combine(directory, "notes.txt"), "v1", Ct);
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        await File.WriteAllTextAsync(Path.Combine(directory, "src", "app.js"), "console.log(1)", Ct);
        await File.WriteAllTextAsync(Path.Combine(directory, ".env"), "TOKEN=secret", Ct);
        Directory.CreateDirectory(Path.Combine(directory, "node_modules", "pkg"));
        await File.WriteAllTextAsync(Path.Combine(directory, "node_modules", "pkg", "index.js"), "x", Ct);
        await File.WriteAllTextAsync(Path.Combine(directory, "bad:name.txt"), "not allowed on OneDrive", Ct);

        status = await SyncNowAsync(client, conversation.Id);

        Assert.Equal(OneDriveSyncState.Synced, status.State);
        Assert.Contains("略過 1 個", status.LastError, StringComparison.Ordinal);
        var drive = factory.Oidc.Graph.DriveOf("od-sync-amy");
        var folder = $"{Root}/{status.FolderPath}";
        Assert.Equal(["notes.txt", "src/app.js"], drive.ListFiles(folder));
        Assert.Equal("v1"u8.ToArray(), drive.ReadFile($"{folder}/notes.txt"));

        // 使用者在 OneDrive 上修改、新增檔案：下一次執行前下載到工作目錄。
        drive.WriteFile($"{folder}/notes.txt", "cloud v2"u8.ToArray());
        drive.WriteFile($"{folder}/docs/讀我.md", "# hi"u8.ToArray());
        before = DateTimeOffset.UtcNow.AddSeconds(-1);

        await RunAsync(client, conversation.Id, "again");

        Assert.Equal("cloud v2", await File.ReadAllTextAsync(Path.Combine(directory, "notes.txt"), Ct));
        Assert.Equal("# hi", await File.ReadAllTextAsync(Path.Combine(directory, "docs", "讀我.md"), Ct));
        status = await WaitForSyncAsync(client, conversation.Id, before);
        Assert.Equal(0, status.ConflictCount);
        // 下載的檔案不會被當成本機修改再上傳一次。
        Assert.Equal("cloud v2"u8.ToArray(), drive.ReadFile($"{folder}/notes.txt"));
    }

    [Fact]
    public async Task ConflictingEdits_KeepBothVersions_OnBothSides()
    {
        var (client, userId) = await ConnectedUserAsync("od-sync-ben");
        using var _ = client;
        var conversation = await client.CreateConversationAsync(null, "conflict");
        await RunAsync(client, conversation.Id, "hello");
        var directory = HostDirectory(userId, conversation.Id);
        await File.WriteAllTextAsync(Path.Combine(directory, "plan.md"), "v1", Ct);
        var status = await SyncNowAsync(client, conversation.Id);
        var folder = $"{Root}/{status.FolderPath}";
        var drive = factory.Oidc.Graph.DriveOf("od-sync-ben");

        await File.WriteAllTextAsync(Path.Combine(directory, "plan.md"), "local edit", Ct);
        drive.WriteFile($"{folder}/plan.md", "cloud edit"u8.ToArray());
        status = await SyncNowAsync(client, conversation.Id);

        Assert.Equal(OneDriveSyncState.Synced, status.State);
        Assert.Equal(1, status.ConflictCount);
        // 原檔名是雲端版本，本機版本另存衝突副本；兩邊都有兩份，沒有任何一份被覆蓋。
        Assert.Equal("cloud edit", await File.ReadAllTextAsync(Path.Combine(directory, "plan.md"), Ct));
        var copy = Assert.Single(Directory.GetFiles(directory, "plan (OneDrive 衝突 *).md"));
        Assert.Equal("local edit", await File.ReadAllTextAsync(copy, Ct));
        var cloudFiles = drive.ListFiles(folder);
        Assert.Equal(2, cloudFiles.Count);
        Assert.Equal("cloud edit"u8.ToArray(), drive.ReadFile($"{folder}/plan.md"));
        Assert.Equal("local edit"u8.ToArray(), drive.ReadFile($"{folder}/{Path.GetFileName(copy)}"));
    }

    [Fact]
    public async Task LargeFiles_UseAnUploadSession_AndProjectsMapToTheirOwnFolder()
    {
        var (client, userId) = await ConnectedUserAsync("od-sync-cat");
        using var _ = client;
        var project = await client.CreateProjectAsync("網站");
        var conversation = await client.CreateConversationAsync(project.Id, "big");
        await RunAsync(client, conversation.Id, "hello");
        var content = RandomNumberGenerator.GetBytes((5 * 1024 * 1024) + 123);
        await File.WriteAllBytesAsync(Path.Combine(HostDirectory(userId, conversation.Id, project.Id), "video.bin"), content, Ct);
        var sessions = factory.Oidc.Graph.UploadSessionsCreated;

        var status = await SyncNowAsync(client, conversation.Id);

        Assert.Equal(OneDriveSyncState.Synced, status.State);
        Assert.Equal($"projects/網站-{project.Id.ToString("N")[..8]}", status.FolderPath);
        Assert.Equal(content, factory.Oidc.Graph.DriveOf("od-sync-cat").ReadFile($"{Root}/{status.FolderPath}/video.bin"));
        Assert.True(factory.Oidc.Graph.UploadSessionsCreated > sessions);
    }

    [Fact]
    public async Task RevokedAuthorization_StopsSync_AndKeepsLocalFiles()
    {
        var (client, userId) = await ConnectedUserAsync("od-sync-dan");
        using var _ = client;
        var conversation = await client.CreateConversationAsync(null, "reauth");
        await RunAsync(client, conversation.Id, "hello");
        var directory = HostDirectory(userId, conversation.Id);
        await File.WriteAllTextAsync(Path.Combine(directory, "keep.txt"), "local", Ct);

        factory.Oidc.Issuer.RevokeRefreshTokens("od-sync-dan");
        factory.Services.GetRequiredService<OneDriveAccessTokenCache>().Remove(userId);
        var status = await SyncNowAsync(client, conversation.Id);

        Assert.Equal(OneDriveSyncState.Failed, status.State);
        Assert.Contains("重新連結", status.LastError, StringComparison.Ordinal);
        Assert.Equal("local", await File.ReadAllTextAsync(Path.Combine(directory, "keep.txt"), Ct));
        Assert.Equal(OneDriveAvailability.NeedsReauth, (await StatusAsync(client, conversation.Id))!.Availability);
        using var retry = await client.PostAsync(new Uri($"/api/conversations/{conversation.Id}/onedrive/sync", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        // 執行不受影響：沒有啟用同步時照常執行。
        var events = await RunAsync(client, conversation.Id, "still works");
        Assert.DoesNotContain(events, data => data.Contains("OneDrive", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithoutCapability_NothingIsSynced()
    {
        using var client = await factory.LoginWithOidcAsync("od-sync-eve");
        await client.GetFromJsonAsync<JsonElement>("/api/me", Ct); // 取得 XSRF token
        var conversation = await client.CreateConversationAsync(null, "no onedrive");

        var events = await RunAsync(client, conversation.Id, "hello");
        using var sync = await client.PostAsync(new Uri($"/api/conversations/{conversation.Id}/onedrive/sync", UriKind.Relative), null, Ct);

        Assert.DoesNotContain(events, data => data.Contains("OneDrive", StringComparison.Ordinal));
        Assert.Equal(OneDriveAvailability.NotAllowed, (await StatusAsync(client, conversation.Id))!.Availability);
        Assert.Equal(HttpStatusCode.Conflict, sync.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<IVibeMakerDbContext>().OneDriveSyncScopes.AnyAsync(s => s.ScopeId == conversation.Id, Ct));
    }

    [Fact]
    public async Task StatusAndSync_NeverExposeTokensOrItemIds()
    {
        var (client, _) = await ConnectedUserAsync("od-sync-fay");
        using var __ = client;
        var conversation = await client.CreateConversationAsync(null, "no leak");
        await RunAsync(client, conversation.Id, "hello");
        await SyncNowAsync(client, conversation.Id);

        var body = await client.GetStringAsync($"/api/conversations/{conversation.Id}/onedrive", Ct);

        Assert.DoesNotContain("fake-at-", body, StringComparison.Ordinal);
        Assert.DoesNotContain("fake-rt-", body, StringComparison.Ordinal);
        await using var scope = factory.Services.CreateAsyncScope();
        var folderItemId = await scope.ServiceProvider.GetRequiredService<IVibeMakerDbContext>().OneDriveSyncScopes
            .Where(s => s.ScopeId == conversation.Id).Select(s => s.FolderItemId).SingleAsync(Ct);
        Assert.DoesNotContain(folderItemId, body, StringComparison.Ordinal);
        Assert.DoesNotContain("/workspace", body, StringComparison.Ordinal);
    }
}
