using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.Testing.FakeLlm;
using Ymir.VibeMaker.Application.Knowledge;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Domain;

namespace Ymir.IntegrationTests.Knowledge;

/// <summary>API + Fake LLM（<c>/v1/embeddings</c>）：專案知識庫的索引流程（ADR-0014）。</summary>
public sealed class KnowledgeApiFactory : ApiFactory
{
    public const string EmbeddingModel = "fake-embedding";

    private readonly Lazy<FakeLlmServer> _fakeLlm = new(() => FakeLlmServer.StartAsync().GetAwaiter().GetResult());

    public string KnowledgeRoot { get; } = Path.Combine(Path.GetTempPath(), "ymir-kb-" + Guid.NewGuid().ToString("N"));

    public FakeLlmState FakeLlm => _fakeLlm.Value.State;

    protected override void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting("VibeMaker:Pi:ModelBaseUrl", _fakeLlm.Value.BaseUrl.ToString());
        builder.UseSetting("VibeMaker:Pi:ModelId", FakeLlmEndpoints.ModelId);
        builder.UseSetting("VibeMaker:Rag:EmbeddingModel", EmbeddingModel);
        builder.UseSetting("VibeMaker:Rag:PollInterval", "00:00:00.500");
        builder.UseSetting("VibeMaker:Rag:ChunkSize", "200");
        builder.UseSetting("VibeMaker:Rag:ChunkOverlap", "20");
        builder.UseSetting("VibeMaker:Rag:MaxFileBytes", "1048576");
        builder.UseSetting("Ymir:Knowledge:Root", KnowledgeRoot);
        builder.UseSetting("VibeMaker:Models:0:Id", FakeLlmEndpoints.ModelId);
        builder.UseSetting("VibeMaker:Models:0:AllowKnowledgeBase", "true");
        builder.UseSetting("VibeMaker:Models:1:Id", RestrictedModel);
    }

    /// <summary>沒有被管理員標記可用於知識庫的模型。</summary>
    public const string RestrictedModel = "restricted-model";

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_fakeLlm.IsValueCreated)
        {
            await _fakeLlm.Value.DisposeAsync();
        }

        if (Directory.Exists(KnowledgeRoot))
        {
            Directory.Delete(KnowledgeRoot, recursive: true);
        }
    }
}

public class KnowledgeBaseTests(KnowledgeApiFactory factory) : IClassFixture<KnowledgeApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid projectId, string fileName, byte[] content)
    {
        using var body = new ByteArrayContent(content);
        body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        return await client.PostAsync(new Uri($"/api/projects/{projectId}/knowledge/documents?fileName={Uri.EscapeDataString(fileName)}", UriKind.Relative), body, Ct);
    }

    internal static async Task<KnowledgeDocumentResponse> UploadReadyAsync(HttpClient client, Guid projectId, string fileName, string text)
    {
        using var upload = await UploadAsync(client, projectId, fileName, Encoding.UTF8.GetBytes(text));
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var document = (await upload.Content.ReadFromJsonAsync<KnowledgeDocumentResponse>(JsonDefaults.Options, Ct))!;
        return await WaitAsync(client, projectId, document.Id);
    }

    internal static async Task<KnowledgeDocumentResponse> WaitAsync(HttpClient client, Guid projectId, Guid documentId)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (true)
        {
            var kb = await client.GetFromJsonAsync<KnowledgeBaseResponse>($"/api/projects/{projectId}/knowledge", JsonDefaults.Options, Ct);
            var document = kb!.Documents.SingleOrDefault(d => d.Id == documentId);
            if (document is { Status: KnowledgeDocumentStatus.Ready or KnowledgeDocumentStatus.Failed })
            {
                return document;
            }

            await Task.Delay(200, timeout.Token);
        }
    }

    internal static byte[] Docx(params string[] paragraphs)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("""<?xml version="1.0" encoding="UTF-8"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>""");
            foreach (var paragraph in paragraphs)
            {
                writer.Write($"<w:p><w:r><w:t>{System.Security.SecurityElement.Escape(paragraph)}</w:t></w:r></w:p>");
            }

            writer.Write("</w:body></w:document>");
        }

        return buffer.ToArray();
    }

    private async Task<IReadOnlyList<VectorHit>> SearchAsync(Guid userId, Guid projectId, IReadOnlyCollection<Guid> documentIds, string query)
    {
        var store = factory.Services.GetRequiredService<IVectorStore>();
        return await store.SearchAsync(userId, projectId, documentIds, FakeEmbedding.Embed(query), 10, Ct);
    }

    private static async Task<Guid> UserIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/me", Ct)).GetProperty("id").GetGuid();

    [Fact]
    public async Task TextMarkdownAndDocx_AreIndexed()
    {
        using var client = await factory.LoginAsync($"kb-index-{Guid.NewGuid():N}");
        var project = await client.CreateProjectAsync("知識庫");

        var text = await UploadReadyAsync(client, project.Id, "規章.txt", "第一條：員工每年有十四天特別休假。\n\n第二條：加班需事先申請。");
        var markdown = await UploadReadyAsync(client, project.Id, "readme.md", "# 系統說明\n\n部署前先備份資料庫。");
        using var docxUpload = await UploadAsync(client, project.Id, "手冊.docx", Docx("報銷流程：先填寫申請單。", "主管核准後送財務。"));
        var docx = await WaitAsync(client, project.Id, (await docxUpload.Content.ReadFromJsonAsync<KnowledgeDocumentResponse>(JsonDefaults.Options, Ct))!.Id);

        Assert.All([text, markdown, docx], d => Assert.Equal(KnowledgeDocumentStatus.Ready, d.Status));
        Assert.All([text, markdown, docx], d => Assert.True(d.ChunkCount > 0));
        var hits = await SearchAsync(await UserIdAsync(client), project.Id, [text.Id, markdown.Id, docx.Id], "報銷流程申請單");
        Assert.Equal(docx.Id, hits[0].DocumentId);
        Assert.Contains("報銷流程", hits[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidDocuments_GetSummaryErrors_WithoutFakeSuccess()
    {
        using var client = await factory.LoginAsync($"kb-invalid-{Guid.NewGuid():N}");
        var project = await client.CreateProjectAsync("invalid");

        using var unsupported = await UploadAsync(client, project.Id, "tool.exe", [1, 2, 3]);
        using var tooLarge = await UploadAsync(client, project.Id, "big.txt", new byte[1024 * 1024 + 10]);
        var blank = await UploadReadyAsync(client, project.Id, "blank.txt", "   \n\n  ");
        using var brokenPdf = await UploadAsync(client, project.Id, "broken.pdf", Encoding.ASCII.GetBytes("%PDF-1.4 not really"));
        var pdf = await WaitAsync(client, project.Id, (await brokenPdf.Content.ReadFromJsonAsync<KnowledgeDocumentResponse>(JsonDefaults.Options, Ct))!.Id);

        Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        Assert.Equal(KnowledgeDocumentStatus.Failed, blank.Status);
        Assert.Contains("沒有可擷取的文字", blank.Error, StringComparison.Ordinal);
        Assert.Equal(KnowledgeDocumentStatus.Failed, pdf.Status);
        Assert.DoesNotContain(factory.KnowledgeRoot, pdf.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NewVersion_ReplacesTheOldOne_AndRemovedDocumentsAreNeverRecalled()
    {
        using var client = await factory.LoginAsync($"kb-version-{Guid.NewGuid():N}");
        var userId = await UserIdAsync(client);
        var project = await client.CreateProjectAsync("versions");

        var v1 = await UploadReadyAsync(client, project.Id, "價目表.txt", "舊價格：每小時一千元。");
        var v2 = await UploadReadyAsync(client, project.Id, "價目表.txt", "新價格：每小時兩千元。");

        Assert.Equal(2, v2.Version);
        var kb = await client.GetFromJsonAsync<KnowledgeBaseResponse>($"/api/projects/{project.Id}/knowledge", JsonDefaults.Options, Ct);
        Assert.Equal([v2.Id], kb!.Documents.Select(d => d.Id));
        // 舊版本的段落已刪除：即使把舊的 id 也交給搜尋，也只會找到新版本。
        Assert.All(await SearchAsync(userId, project.Id, [v1.Id, v2.Id], "價格"), h => Assert.Equal(v2.Id, h.DocumentId));

        using var remove = await client.DeleteAsync(new Uri($"/api/projects/{project.Id}/knowledge/documents/{v2.Id}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        Assert.Empty(await SearchAsync(userId, project.Id, [v2.Id], "價格"));
        Assert.Empty((await client.GetFromJsonAsync<KnowledgeBaseResponse>($"/api/projects/{project.Id}/knowledge", JsonDefaults.Options, Ct))!.Documents);
    }

    [Fact]
    public async Task Projects_AreIsolated_AndOtherUsersGet404()
    {
        using var alice = await factory.LoginAsync($"kb-alice-{Guid.NewGuid():N}");
        using var bob = await factory.LoginAsync($"kb-bob-{Guid.NewGuid():N}");
        var aliceId = await UserIdAsync(alice);
        var projectA = await alice.CreateProjectAsync("A");
        var projectB = await alice.CreateProjectAsync("B");
        var docA = await UploadReadyAsync(alice, projectA.Id, "a.txt", "專案 A 的祕密：藍色計畫。");

        // 同一使用者的另一個專案：該專案的 SQLite 沒有 A 的段落。
        Assert.Empty(await SearchAsync(aliceId, projectB.Id, [docA.Id], "藍色計畫"));
        using var bobGet = await bob.GetAsync(new Uri($"/api/projects/{projectA.Id}/knowledge", UriKind.Relative), Ct);
        using var bobUpload = await UploadAsync(bob, projectA.Id, "x.txt", "intrusion"u8.ToArray());
        using var bobRemove = await bob.DeleteAsync(new Uri($"/api/projects/{projectA.Id}/knowledge/documents/{docA.Id}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NotFound, bobGet.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, bobUpload.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, bobRemove.StatusCode);
        Assert.Equal(KnowledgeDocumentStatus.Ready, (await WaitAsync(alice, projectA.Id, docA.Id)).Status);
    }

    [Fact]
    public async Task InterruptedIndexing_IsRedoneAfterRestart_WithoutDuplicates()
    {
        using var client = await factory.LoginAsync($"kb-restart-{Guid.NewGuid():N}");
        var userId = await UserIdAsync(client);
        var project = await client.CreateProjectAsync("restart");
        var document = await UploadReadyAsync(client, project.Id, "notes.txt", "會議結論：下週上線。");

        // 模擬服務在索引途中中斷：狀態停在 Indexing。
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IVibeMakerDbContext>();
            var entity = await db.KnowledgeDocuments.SingleAsync(d => d.Id == document.Id, Ct);
            entity.StartIndexing(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(Ct);
            await scope.ServiceProvider.GetRequiredService<KnowledgeBaseService>().ReconcileAsync(Ct);
        }

        factory.Services.GetRequiredService<KnowledgeSignal>().Notify();
        var redone = await WaitAsync(client, project.Id, document.Id);

        Assert.Equal(KnowledgeDocumentStatus.Ready, redone.Status);
        Assert.Equal(document.ChunkCount, (await SearchAsync(userId, project.Id, [document.Id], "會議結論")).Count);
    }
}

/// <summary>沒有設定 Embedding 模型時知識庫停用。</summary>
public class KnowledgeDisabledTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Upload_IsRejected_WhenNoEmbeddingModelIsConfigured()
    {
        using var client = await factory.LoginAsync($"kb-disabled-{Guid.NewGuid():N}");
        var project = await client.CreateProjectAsync("disabled");

        using var upload = await KnowledgeBaseTests.UploadAsync(client, project.Id, "a.txt", "hi"u8.ToArray());
        var kb = await client.GetFromJsonAsync<KnowledgeBaseResponse>($"/api/projects/{project.Id}/knowledge", JsonDefaults.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, upload.StatusCode);
        Assert.False(kb!.Enabled);
    }
}
