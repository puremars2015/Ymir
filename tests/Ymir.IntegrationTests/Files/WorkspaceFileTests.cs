using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.Executions;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Contracts.Executions;
using Ymir.VibeMaker.Contracts.Files;
using Ymir.VibeMaker.Infrastructure.Runtime;

namespace Ymir.IntegrationTests.Files;

/// <summary>對話的檔案：列出、下載、打包；路徑穿越與 symlink 逃逸一律 404（SA §12、CLAUDE.md 安全紅線）。</summary>
public class WorkspaceFileTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> UserIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/me", Ct)).GetProperty("id").GetGuid();

    /// <summary>送一則訊息（建立 runtime 與工作目錄），回傳工作目錄的 host 路徑。</summary>
    private async Task<(Guid ConversationId, string Directory)> PrepareAsync(HttpClient client, Guid? projectId = null)
    {
        var conversation = await client.CreateConversationAsync(projectId, "files");
        var (_, sent) = await client.SendMessageAsync(conversation.Id, "hello");
        var events = await client.ReadEventsAsync(sent!.EventStreamUrl);
        Assert.Equal(ExecutionEventNames.ExecutionCompleted, events[^1].EventType);
        var directories = UserDirectories.For(factory.WorkspaceRoot, await UserIdAsync(client));
        var directory = directories.HostPathOf(RuntimePaths.WorkingDirectoryFor(conversation.Id, projectId));
        Directory.CreateDirectory(directory);
        return (conversation.Id, directory);
    }

    [Fact]
    public async Task NewConversation_HasNoFiles_WithoutStartingARuntime()
    {
        using var client = await factory.LoginAsync($"files-new-{Guid.NewGuid():N}");
        var conversation = await client.CreateConversationAsync(null, "empty");

        var files = await client.GetFromJsonAsync<WorkspaceFilesResponse>($"/api/conversations/{conversation.Id}/files", JsonDefaults.Options, Ct);

        Assert.Empty(files!.Files);
    }

    [Fact]
    public async Task Files_AreListedAndDownloadable_AsAttachments()
    {
        using var client = await factory.LoginAsync($"files-dl-{Guid.NewGuid():N}");
        var (conversationId, directory) = await PrepareAsync(client);
        await File.WriteAllTextAsync(Path.Combine(directory, "calculator.html"), "<script>alert(1)</script>", Ct);
        Directory.CreateDirectory(Path.Combine(directory, "src", "中文"));
        await File.WriteAllBytesAsync(Path.Combine(directory, "src", "中文", "資料 1.bin"), [0, 1, 2, 255, 10, 13], Ct);
        Directory.CreateDirectory(Path.Combine(directory, ".git"));
        await File.WriteAllTextAsync(Path.Combine(directory, ".git", "config"), "secret", Ct);
        await File.WriteAllTextAsync(Path.Combine(directory, ".env"), "TOKEN=x", Ct);
        Directory.CreateDirectory(Path.Combine(directory, "node_modules", "pkg"));
        await File.WriteAllTextAsync(Path.Combine(directory, "node_modules", "pkg", "index.js"), "x", Ct);

        var files = await client.GetFromJsonAsync<WorkspaceFilesResponse>($"/api/conversations/{conversationId}/files", JsonDefaults.Options, Ct);

        Assert.Equal(["calculator.html", "src/中文/資料 1.bin"], files!.Files.Select(f => f.Path));
        Assert.Equal(6, files.Files[1].Size);
        Assert.False(files.Truncated);

        using var html = await client.GetAsync($"/api/conversations/{conversationId}/files/download?path=calculator.html", Ct);
        Assert.Equal(HttpStatusCode.OK, html.StatusCode);
        Assert.Equal("application/octet-stream", html.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", html.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("calculator.html", html.Content.Headers.ContentDisposition.FileNameStar);
        Assert.Equal("nosniff", html.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("sandbox", html.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("<script>alert(1)</script>", await html.Content.ReadAsStringAsync(Ct));

        using var binary = await client.GetAsync($"/api/conversations/{conversationId}/files/download?path={Uri.EscapeDataString("src/中文/資料 1.bin")}", Ct);
        Assert.Equal(new byte[] { 0, 1, 2, 255, 10, 13 }, await binary.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal("資料 1.bin", binary.Content.Headers.ContentDisposition!.FileNameStar);
    }

    [Fact]
    public async Task Archive_ZipsEveryListedFile()
    {
        using var client = await factory.LoginAsync($"files-zip-{Guid.NewGuid():N}");
        var project = await client.CreateProjectAsync("zip 專案");
        var (conversationId, directory) = await PrepareAsync(client, project.Id);
        await File.WriteAllTextAsync(Path.Combine(directory, "index.html"), "<h1>hi</h1>", Ct);
        Directory.CreateDirectory(Path.Combine(directory, "css"));
        await File.WriteAllTextAsync(Path.Combine(directory, "css", "site.css"), "body{}", Ct);
        await File.WriteAllTextAsync(Path.Combine(directory, ".env"), "TOKEN=x", Ct);

        using var response = await client.GetAsync($"/api/conversations/{conversationId}/files/archive", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("files.zip", response.Content.Headers.ContentDisposition!.FileNameStar);
        using var zip = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync(Ct)));
        Assert.Equal(["css/site.css", "index.html"], zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
        using var reader = new StreamReader(zip.GetEntry("index.html")!.Open());
        Assert.Equal("<h1>hi</h1>", await reader.ReadToEndAsync(Ct));
    }

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("/etc/passwd")]
    [InlineData("a/../calculator.html")]
    [InlineData(".env")]
    [InlineData(".git/config")]
    [InlineData("node_modules/pkg/index.js")]
    [InlineData("missing.txt")]
    [InlineData("")]
    public async Task UnsafeOrMissingPaths_Return404(string path)
    {
        using var client = await factory.LoginAsync($"files-bad-{Guid.NewGuid():N}");
        var (conversationId, directory) = await PrepareAsync(client);
        await File.WriteAllTextAsync(Path.Combine(directory, "calculator.html"), "ok", Ct);
        await File.WriteAllTextAsync(Path.Combine(directory, ".env"), "TOKEN=x", Ct);

        using var response = await client.GetAsync($"/api/conversations/{conversationId}/files/download?path={Uri.EscapeDataString(path)}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Symlinks_CannotEscapeTheWorkingDirectory()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "symlink 測試只在 Linux / macOS 執行");
        using var client = await factory.LoginAsync($"files-link-{Guid.NewGuid():N}");
        var (conversationId, directory) = await PrepareAsync(client);
        var outside = Path.Combine(Path.GetTempPath(), $"ymir-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "secret.txt"), "host secret", Ct);
        File.CreateSymbolicLink(Path.Combine(directory, "leak.txt"), Path.Combine(outside, "secret.txt"));
        Directory.CreateSymbolicLink(Path.Combine(directory, "outside"), outside);
        await File.WriteAllTextAsync(Path.Combine(directory, "real.txt"), "ok", Ct);

        var files = await client.GetFromJsonAsync<WorkspaceFilesResponse>($"/api/conversations/{conversationId}/files", JsonDefaults.Options, Ct);
        using var leak = await client.GetAsync($"/api/conversations/{conversationId}/files/download?path=leak.txt", Ct);
        using var throughDirectory = await client.GetAsync($"/api/conversations/{conversationId}/files/download?path=outside/secret.txt", Ct);
        using var archive = await client.GetAsync($"/api/conversations/{conversationId}/files/archive", Ct);

        Assert.Equal(["real.txt"], files!.Files.Select(f => f.Path));
        Assert.Equal(HttpStatusCode.NotFound, leak.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, throughDirectory.StatusCode);
        using var zip = new ZipArchive(new MemoryStream(await archive.Content.ReadAsByteArrayAsync(Ct)));
        Assert.Equal(["real.txt"], zip.Entries.Select(e => e.FullName));
        Directory.Delete(outside, recursive: true);
    }

    [Fact]
    public async Task OtherUsersConversation_Returns404()
    {
        using var owner = await factory.LoginAsync($"files-owner-{Guid.NewGuid():N}");
        var (conversationId, directory) = await PrepareAsync(owner);
        await File.WriteAllTextAsync(Path.Combine(directory, "mine.txt"), "private", Ct);
        using var intruder = await factory.LoginAsync($"files-intruder-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/api/conversations/{conversationId}/files", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/api/conversations/{conversationId}/files/download?path=mine.txt", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/api/conversations/{conversationId}/files/archive", Ct)).StatusCode);
    }
}
