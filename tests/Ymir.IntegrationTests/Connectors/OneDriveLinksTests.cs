using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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

public class OneDriveLinksTests(OneDriveApiFactory factory) : IClassFixture<OneDriveApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(HttpClient Client, Guid UserId, string Account)> UserAsync()
    {
        var account = "od-links-" + Guid.NewGuid().ToString("N");
        var client = await factory.LoginWithOidcAsync(account);
        var userId = (await client.GetFromJsonAsync<JsonElement>("/api/me", Ct)).GetProperty("id").GetGuid();
        await AllowAsync(userId, ExtensionGrantSetting.Allow);
        Assert.Equal("/settings?onedrive=connected", await OneDriveApiFactory.ConnectOneDriveAsync(client, account));
        return (client, userId, account);
    }

    private async Task AllowAsync(Guid userId, ExtensionGrantSetting grant)
    {
        using var admin = await factory.LoginAsync("od-links-admin-" + Guid.NewGuid().ToString("N"), UserRole.Admin);
        using var response = await admin.PutAsJsonAsync($"/api/admin/users/{userId}/extensions",
            new SaveUserExtensionsRequest(ExtensionGrantSetting.Inherit, ExtensionGrantSetting.Inherit, ExtensionGrantSetting.Inherit, grant), JsonDefaults.Options, Ct);
        response.EnsureSuccessStatusCode();
    }

    private static Task<OneDriveLinkedItemInfo[]?> LinksAsync(HttpClient client, Guid conversationId) =>
        client.GetFromJsonAsync<OneDriveLinkedItemInfo[]>($"/api/conversations/{conversationId}/onedrive/links", JsonDefaults.Options, Ct);

    private static async Task<OneDriveLinkedItemInfo> AddAsync(HttpClient client, Guid conversationId, string itemId, bool readOnly = false)
    {
        using var response = await client.PostAsJsonAsync($"/api/conversations/{conversationId}/onedrive/links", new AddOneDriveLinkRequest(itemId, readOnly), JsonDefaults.Options, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OneDriveLinkedItemInfo>(JsonDefaults.Options, Ct))!;
    }

    private static async Task<string> ItemAsync(HttpClient client, string name)
    {
        var items = await client.GetFromJsonAsync<OneDriveBrowserItem[]>("/api/connectors/onedrive/search?q=" + Uri.EscapeDataString(name), JsonDefaults.Options, Ct);
        return Assert.Single(items!, item => item.Name == name).Id;
    }

    private string HostPath(Guid userId, Guid conversationId, string path, Guid? projectId = null) =>
        Path.Combine(UserDirectories.For(factory.WorkspaceRoot, userId).HostPathOf(RuntimePaths.WorkingDirectoryFor(conversationId, projectId)), path);

    private static async Task<string> RunAsync(HttpClient client, Guid conversationId)
    {
        var (response, sent) = await client.SendMessageAsync(conversationId, "read the linked documents");
        using var _ = response;
        response.EnsureSuccessStatusCode();
        var events = await client.ReadEventsAsync(sent!.EventStreamUrl);
        Assert.Equal(ExecutionEventNames.ExecutionCompleted, events[^1].EventType);
        return string.Join('\n', events.Select(e => e.Data.ToString()));
    }

    [Fact]
    public async Task FileLink_DownloadsBeforeAgent_WithoutRoot_AndProtectsLocalEdits()
    {
        var (client, userId, account) = await UserAsync();
        using var _ = client;
        var drive = factory.Oidc.Graph.DriveOf(account);
        drive.WriteFile("/Documents/plan.md", "cloud v1"u8.ToArray());
        var conversation = await client.CreateConversationAsync(null, "linked");
        var itemId = await ItemAsync(client, "plan.md");
        var link = await AddAsync(client, conversation.Id, itemId);
        Assert.StartsWith("onedrive/", link.LocalPath, StringComparison.Ordinal);
        Assert.False(link.WriteBackEnabled);
        Assert.Equal(link.Id, (await AddAsync(client, conversation.Id, itemId)).Id);

        var body = await RunAsync(client, conversation.Id);
        Assert.Contains(link.LocalPath, body, StringComparison.Ordinal);
        Assert.DoesNotContain("fake-at-", body, StringComparison.Ordinal);
        var file = HostPath(userId, conversation.Id, link.LocalPath);
        Assert.Equal("cloud v1", await File.ReadAllTextAsync(file, Ct));
        Assert.Equal(OneDriveLinkedItemState.Downloaded, Assert.Single((await LinksAsync(client, conversation.Id))!).State);

        await File.WriteAllTextAsync(file, "local edit", Ct);
        await RunAsync(client, conversation.Id);
        Assert.Equal("local edit", await File.ReadAllTextAsync(file, Ct));
        Assert.Equal("cloud v1"u8.ToArray(), drive.ReadFile("/Documents/plan.md"));

        drive.WriteFile("/Documents/plan.md", "cloud edit"u8.ToArray());
        await RunAsync(client, conversation.Id);
        Assert.Equal("cloud edit", await File.ReadAllTextAsync(file, Ct));
        var conflict = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(file)!, "*OneDrive 衝突*"));
        Assert.Equal("local edit", await File.ReadAllTextAsync(conflict, Ct));
        Assert.Equal(1, Assert.Single((await LinksAsync(client, conversation.Id))!).ConflictCount);
    }

    [Fact]
    public async Task FolderLink_SharesAcrossProjectConversations_ButNotUsersOrOtherScopes()
    {
        var (client, userId, account) = await UserAsync();
        using var _ = client;
        factory.Oidc.Graph.DriveOf(account).WriteFile("/Reports/sub/budget.txt", "budget"u8.ToArray());
        factory.Oidc.Graph.DriveOf(account).WriteFile("/Reports/.env", "secret"u8.ToArray());
        var project = await client.CreateProjectAsync("shared");
        var first = await client.CreateConversationAsync(project.Id, "first");
        var second = await client.CreateConversationAsync(project.Id, "second");
        var link = await AddAsync(client, first.Id, await ItemAsync(client, "Reports"));
        Assert.Equal(link.Id, Assert.Single((await LinksAsync(client, second.Id))!).Id);
        await RunAsync(client, second.Id);
        Assert.Equal("budget", await File.ReadAllTextAsync(HostPath(userId, second.Id, link.LocalPath + "/sub/budget.txt", project.Id), Ct));
        Assert.False(File.Exists(HostPath(userId, second.Id, link.LocalPath + "/.env", project.Id)));

        var ownOther = await client.CreateConversationAsync(null, "other");
        using var wrongScope = await client.PatchAsJsonAsync($"/api/conversations/{ownOther.Id}/onedrive/links/{link.Id}", new SetOneDriveLinkReadOnlyRequest(true), Ct);
        Assert.Equal(HttpStatusCode.NotFound, wrongScope.StatusCode);
        var (intruder, _, _) = await UserAsync();
        using var intruderClient = intruder;
        using var get = await intruder.GetAsync(new Uri($"/api/conversations/{first.Id}/onedrive/links", UriKind.Relative), Ct);
        using var delete = await intruder.DeleteAsync(new Uri($"/api/conversations/{first.Id}/onedrive/links/{link.Id}", UriKind.Relative), Ct);
        using var patch = await intruder.PatchAsJsonAsync($"/api/conversations/{first.Id}/onedrive/links/{link.Id}", new SetOneDriveLinkReadOnlyRequest(true), Ct);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
    }

    [Fact]
    public async Task Root_SyncFolder_AndOverlappingLinks_AreRejected()
    {
        var (client, _, account) = await UserAsync();
        using var _ = client;
        var drive = factory.Oidc.Graph.DriveOf(account);
        drive.WriteFile("/Documents/a.txt", "a"u8.ToArray());
        drive.WriteFile("/Ymir/inside.txt", "inside"u8.ToArray());
        using var root = await client.PutAsJsonAsync("/api/connectors/onedrive/root", new SetOneDriveRootRequest("/Ymir"), Ct);
        root.EnsureSuccessStatusCode();
        var conversation = await client.CreateConversationAsync(null, "blocked roots");
        foreach (var itemId in new[] { drive.RootId, await ItemAsync(client, "Ymir"), await ItemAsync(client, "inside.txt") })
        {
            using var refused = await client.PostAsJsonAsync($"/api/conversations/{conversation.Id}/onedrive/links", new AddOneDriveLinkRequest(itemId), Ct);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }
        await AddAsync(client, conversation.Id, await ItemAsync(client, "Documents"));
        using var overlap = await client.PostAsJsonAsync($"/api/conversations/{conversation.Id}/onedrive/links", new AddOneDriveLinkRequest(await ItemAsync(client, "a.txt")), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, overlap.StatusCode);
    }

    [Fact]
    public async Task ReadOnlyRestoresCloudCopy_AndUnlinkPreservesFilesAndCloud()
    {
        var (client, userId, account) = await UserAsync();
        using var _ = client;
        var drive = factory.Oidc.Graph.DriveOf(account);
        drive.WriteFile("/readme", "cloud"u8.ToArray());
        var conversation = await client.CreateConversationAsync(null, "read only");
        var link = await AddAsync(client, conversation.Id, await ItemAsync(client, "readme"));
        using var toggle = await client.PatchAsJsonAsync($"/api/conversations/{conversation.Id}/onedrive/links/{link.Id}", new SetOneDriveLinkReadOnlyRequest(true), Ct);
        toggle.EnsureSuccessStatusCode();
        Assert.True((await toggle.Content.ReadFromJsonAsync<OneDriveLinkedItemInfo>(JsonDefaults.Options, Ct))!.ReadOnly);
        await RunAsync(client, conversation.Id);
        var path = HostPath(userId, conversation.Id, link.LocalPath);
        await File.WriteAllTextAsync(path, "local", Ct);
        await RunAsync(client, conversation.Id);
        Assert.Equal("cloud", await File.ReadAllTextAsync(path, Ct));
        Assert.Equal("cloud"u8.ToArray(), drive.ReadFile("/readme"));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, "*OneDrive 衝突*"));
        using var response = await client.DeleteAsync(new Uri($"/api/conversations/{conversation.Id}/onedrive/links/{link.Id}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty((await LinksAsync(client, conversation.Id))!);
        Assert.True(File.Exists(path));
        Assert.NotNull(drive.ReadFile("/readme"));
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<IVibeMakerDbContext>().OneDriveLinkedFiles.AnyAsync(f => f.LinkId == link.Id, Ct));
    }

    [Fact]
    public async Task LinkAndDownloadLimits_AreEnforced_WithoutCloudWrites()
    {
        var settings = factory.Services.GetRequiredService<IOptions<OneDriveSyncOptions>>().Value;
        var oldLinks = settings.MaxLinksPerScope;
        var oldFiles = settings.MaxLinkedFolderFiles;
        try
        {
            settings.MaxLinksPerScope = 1;
            settings.MaxLinkedFolderFiles = 2;
            var (client, _, account) = await UserAsync();
            using var _ = client;
            var drive = factory.Oidc.Graph.DriveOf(account);
            drive.WriteFile("/Folder/a.txt", "a"u8.ToArray());
            drive.WriteFile("/Folder/b.txt", "b"u8.ToArray());
            drive.WriteFile("/Folder/c.txt", "c"u8.ToArray());
            drive.WriteFile("/another.txt", "another"u8.ToArray());
            var conversation = await client.CreateConversationAsync(null, "limits");
            await AddAsync(client, conversation.Id, await ItemAsync(client, "Folder"));
            using var exceeded = await client.PostAsJsonAsync($"/api/conversations/{conversation.Id}/onedrive/links", new AddOneDriveLinkRequest(await ItemAsync(client, "another.txt")), Ct);
            Assert.Equal(HttpStatusCode.Conflict, exceeded.StatusCode);
            await RunAsync(client, conversation.Id);
            var link = Assert.Single((await LinksAsync(client, conversation.Id))!);
            Assert.NotNull(link.LastError);
            await using var scope = factory.Services.CreateAsyncScope();
            Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<IVibeMakerDbContext>().OneDriveLinkedFiles.CountAsync(f => f.LinkId == link.Id, Ct));
            Assert.Equal(3, drive.ListFiles("/Folder").Count);
        }
        finally
        {
            settings.MaxLinksPerScope = oldLinks;
            settings.MaxLinkedFolderFiles = oldFiles;
        }
    }

    [Fact]
    public async Task ExistingOversizeLocalFile_IsNeverOverwritten()
    {
        var (client, userId, account) = await UserAsync();
        using var _ = client;
        var drive = factory.Oidc.Graph.DriveOf(account);
        drive.WriteFile("/keep.txt", "cloud"u8.ToArray());
        var conversation = await client.CreateConversationAsync(null, "oversize");
        var link = await AddAsync(client, conversation.Id, await ItemAsync(client, "keep.txt"), readOnly: true);
        await RunAsync(client, conversation.Id);
        var path = HostPath(userId, conversation.Id, link.LocalPath);
        await File.WriteAllTextAsync(path, new string('x', 30), Ct);
        var settings = factory.Services.GetRequiredService<IOptions<OneDriveSyncOptions>>().Value;
        var old = settings.MaxFileBytes;
        try
        {
            settings.MaxFileBytes = 10;
            await RunAsync(client, conversation.Id);
            Assert.Equal(new string('x', 30), await File.ReadAllTextAsync(path, Ct));
            Assert.Equal(OneDriveLinkedItemState.Failed, Assert.Single((await LinksAsync(client, conversation.Id))!).State);
        }
        finally { settings.MaxFileBytes = old; }
    }

    [Fact]
    public async Task SymlinkOutsideWorkspace_IsRejected_WithoutFailingTheAgent()
    {
        var (client, userId, account) = await UserAsync();
        using var _ = client;
        factory.Oidc.Graph.DriveOf(account).WriteFile("/safe.txt", "cloud"u8.ToArray());
        var conversation = await client.CreateConversationAsync(null, "symlink");
        var link = await AddAsync(client, conversation.Id, await ItemAsync(client, "safe.txt"), readOnly: true);
        await RunAsync(client, conversation.Id);
        var linkedDirectory = Path.GetDirectoryName(HostPath(userId, conversation.Id, link.LocalPath))!;
        var outside = Path.Combine(factory.WorkspaceRoot, "outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        var externalFile = Path.Combine(outside, Path.GetFileName(link.LocalPath));
        await File.WriteAllTextAsync(externalFile, "must remain", Ct);
        Directory.Move(linkedDirectory, linkedDirectory + "-saved");
        Directory.CreateSymbolicLink(linkedDirectory, outside);
        try
        {
            await RunAsync(client, conversation.Id);
            Assert.Equal("must remain", await File.ReadAllTextAsync(externalFile, Ct));
            Assert.Equal(OneDriveLinkedItemState.Failed, Assert.Single((await LinksAsync(client, conversation.Id))!).State);
        }
        finally { Directory.Delete(linkedDirectory); }
    }

    [Fact]
    public async Task CapabilityRevocationStopsDownloads_ButAllowsUnlink()
    {
        var (client, userId, account) = await UserAsync();
        using var _ = client;
        var drive = factory.Oidc.Graph.DriveOf(account);
        drive.WriteFile("/notes.txt", "one"u8.ToArray());
        var conversation = await client.CreateConversationAsync(null, "revocation");
        var link = await AddAsync(client, conversation.Id, await ItemAsync(client, "notes.txt"));
        await RunAsync(client, conversation.Id);
        await AllowAsync(userId, ExtensionGrantSetting.Deny);
        drive.WriteFile("/notes.txt", "two"u8.ToArray());
        await RunAsync(client, conversation.Id);
        Assert.Equal("one", await File.ReadAllTextAsync(HostPath(userId, conversation.Id, link.LocalPath), Ct));
        using var delete = await client.DeleteAsync(new Uri($"/api/conversations/{conversation.Id}/onedrive/links/{link.Id}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }
}
