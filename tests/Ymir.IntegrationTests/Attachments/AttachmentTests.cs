using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.Executions;
using Ymir.VibeMaker.Application.Attachments;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Contracts.Attachments;
using Ymir.VibeMaker.Contracts.Conversations;
using Ymir.VibeMaker.Contracts.Executions;
using Ymir.VibeMaker.Contracts.Files;
using Ymir.VibeMaker.Infrastructure.Runtime;

namespace Ymir.IntegrationTests.Attachments;

/// <summary>訊息附件：上傳到工作目錄、送出時綁定、只能用自己的對話與附件（SA §12）。</summary>
public class AttachmentTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    /// <summary>最小的 1x1 PNG。</summary>
    internal static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid conversationId, string fileName, byte[] content, string contentType = "application/octet-stream")
    {
        using var body = new ByteArrayContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return await client.PostAsync($"/api/conversations/{conversationId}/attachments?fileName={Uri.EscapeDataString(fileName)}", body, Ct);
    }

    private static async Task<AttachmentResponse> UploadOkAsync(HttpClient client, Guid conversationId, string fileName, byte[] content)
    {
        using var response = await UploadAsync(client, conversationId, fileName, content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AttachmentResponse>(JsonDefaults.Options, Ct))!;
    }

    private static async Task<Guid> UserIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/me", Ct)).GetProperty("id").GetGuid();

    [Fact]
    public async Task Upload_StoresFileInWorkingDirectory_AndSendAttachesItToTheMessage()
    {
        using var client = await factory.LoginAsync($"attach-ok-{Guid.NewGuid():N}");
        var conversation = await client.CreateConversationAsync(null, "attachments");

        // 檔名含目錄與 .. 也只會取最後一段；圖片類型以檔頭判斷，不信任瀏覽器的 Content-Type。
        var image = await UploadOkAsync(client, conversation.Id, "../../etc/截圖 1.png", Png);
        var video = await UploadOkAsync(client, conversation.Id, "demo.mp4", [0, 0, 0, 24, 0x66, 0x74, 0x79, 0x70]);
        // 同名檔案馬上再傳一次也不會覆蓋前一個
        var sameName = await UploadOkAsync(client, conversation.Id, "截圖 1.png", Png);
        Assert.NotEqual(image.Path, sameName.Path);

        Assert.Equal("截圖 1.png", image.FileName);
        Assert.StartsWith("uploads/", image.Path, StringComparison.Ordinal);
        Assert.EndsWith("-截圖 1.png", image.Path, StringComparison.Ordinal);
        Assert.Equal("image/png", image.ContentType);
        Assert.Equal(Png.Length, image.Size);
        Assert.Equal("video/mp4", video.ContentType);

        // 還沒送出前也看得到（工作目錄的檔案）
        var directories = UserDirectories.For(factory.WorkspaceRoot, await UserIdAsync(client));
        var hostFile = Path.Combine(directories.HostPathOf(RuntimePaths.ConversationDirectory(conversation.Id)), image.Path);
        Assert.Equal(Png, await File.ReadAllBytesAsync(hostFile, Ct));
        var files = await client.GetFromJsonAsync<WorkspaceFilesResponse>($"/api/conversations/{conversation.Id}/files", JsonDefaults.Options, Ct);
        Assert.Equal(new[] { image.Path, video.Path, sameName.Path }.Order(StringComparer.Ordinal), files!.Files.Select(f => f.Path));
        Assert.DoesNotContain(files.Files, f => f.Path.Contains(".ymir-upload", StringComparison.Ordinal));

        var (_, sent) = await client.SendMessageAsync(conversation.Id, "看一下這張圖", attachmentIds: [image.Id, video.Id]);
        var events = await client.ReadEventsAsync(sent!.EventStreamUrl);
        Assert.Equal(ExecutionEventNames.ExecutionCompleted, events[^1].EventType);

        var messages = await client.GetFromJsonAsync<List<MessageResponse>>($"/api/conversations/{conversation.Id}/messages", JsonDefaults.Options, Ct);
        var user = messages!.Single(m => m.Role == "USER");
        Assert.Equal("看一下這張圖", user.Content); // 對話紀錄只保存使用者輸入的文字
        Assert.Equal([image.Id, video.Id], user.Attachments.Select(a => a.Id));
        // Scripted harness 會回顯送給 Agent 的內容：附件路徑有列在 prompt 中
        var reply = messages!.Single(m => m.Role == "ASSISTANT");
        Assert.Contains(image.Path, reply.Content, StringComparison.Ordinal);
        Assert.Contains(video.Path, reply.Content, StringComparison.Ordinal);
        Assert.Empty(reply.Attachments);

        using var download = await client.GetAsync($"/api/conversations/{conversation.Id}/files/download?path={Uri.EscapeDataString(image.Path)}", Ct);
        Assert.Equal(Png, await download.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task Attachment_CanOnlyBeSentOnce_AndOnlyInItsOwnConversation()
    {
        using var client = await factory.LoginAsync($"attach-once-{Guid.NewGuid():N}");
        var first = await client.CreateConversationAsync(null, "first");
        var second = await client.CreateConversationAsync(null, "second");
        var attachment = await UploadOkAsync(client, first.Id, "notes.txt", "hello"u8.ToArray());

        var (wrongConversation, _) = await client.SendMessageAsync(second.Id, "x", attachmentIds: [attachment.Id]);
        var (unknown, _) = await client.SendMessageAsync(first.Id, "x", attachmentIds: [Guid.NewGuid()]);
        var (ok, sent) = await client.SendMessageAsync(first.Id, "ok", attachmentIds: [attachment.Id]);
        await client.ReadEventsAsync(sent!.EventStreamUrl);
        var (again, _) = await client.SendMessageAsync(first.Id, "again", attachmentIds: [attachment.Id]);

        Assert.Equal(HttpStatusCode.BadRequest, wrongConversation.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, ok.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal(ExecutionErrorCodes.AttachmentNotAvailable, (await again.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task OtherUsersAttachment_CannotBeUsed()
    {
        using var owner = await factory.LoginAsync($"attach-owner-{Guid.NewGuid():N}");
        using var intruder = await factory.LoginAsync($"attach-intruder-{Guid.NewGuid():N}");
        var ownerConversation = await owner.CreateConversationAsync(null, "owner");
        var intruderConversation = await intruder.CreateConversationAsync(null, "intruder");
        var attachment = await UploadOkAsync(owner, ownerConversation.Id, "secret.txt", "secret"u8.ToArray());

        using var upload = await UploadAsync(intruder, ownerConversation.Id, "x.txt", "x"u8.ToArray());
        var (send, _) = await intruder.SendMessageAsync(intruderConversation.Id, "x", attachmentIds: [attachment.Id]);

        Assert.Equal(HttpStatusCode.NotFound, upload.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, send.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("...")]
    [InlineData("/")]
    [InlineData("node_modules")]
    public async Task InvalidFileName_Returns400(string fileName)
    {
        using var client = await factory.LoginAsync($"attach-name-{Guid.NewGuid():N}");
        var conversation = await client.CreateConversationAsync(null, "names");

        using var response = await UploadAsync(client, conversation.Id, fileName, "x"u8.ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ExecutionErrorCodes.AttachmentInvalid, (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task EmptyFile_Returns400()
    {
        using var client = await factory.LoginAsync($"attach-empty-{Guid.NewGuid():N}");
        var conversation = await client.CreateConversationAsync(null, "empty");

        using var response = await UploadAsync(client, conversation.Id, "empty.txt", []);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TooLargeFile_Returns413()
    {
        using var client = await factory.LoginAsync($"attach-large-{Guid.NewGuid():N}");
        var conversation = await client.CreateConversationAsync(null, "large");

        using var response = await UploadAsync(client, conversation.Id, "big.bin", new byte[AttachmentRules.MaxFileBytes + 1]);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(ExecutionErrorCodes.AttachmentTooLarge, (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task UploadsSymlinkPointingOutside_IsRejected()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "symlink 測試只在 Linux / macOS 執行");
        using var client = await factory.LoginAsync($"attach-link-{Guid.NewGuid():N}");
        var conversation = await client.CreateConversationAsync(null, "link");
        // 先上傳一次讓 runtime 與工作目錄存在，再把 uploads 換成指向外面的 symlink（例如 Agent 做的）
        var first = await UploadOkAsync(client, conversation.Id, "a.txt", "a"u8.ToArray());
        var directory = UserDirectories.For(factory.WorkspaceRoot, await UserIdAsync(client)).HostPathOf(RuntimePaths.ConversationDirectory(conversation.Id));
        Directory.Delete(Path.Combine(directory, AttachmentRules.UploadDirectory), recursive: true);
        var outside = Path.Combine(Path.GetTempPath(), $"ymir-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(directory, AttachmentRules.UploadDirectory), outside);

        using var response = await UploadAsync(client, conversation.Id, "b.txt", "b"u8.ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Directory.GetFiles(outside));
        Assert.NotEqual(Guid.Empty, first.Id);
        Directory.Delete(outside, recursive: true);
    }
}
