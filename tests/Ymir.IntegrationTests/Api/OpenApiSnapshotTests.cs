using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ymir.IntegrationTests.Api;

/// <summary>
/// API 契約快照：<c>src/Ymir.Api/openapi/v1.json</c> 是 Angular 產生 TypeScript 型別的來源（<c>npm run api:generate</c>）。
/// API 改了卻沒更新快照時測試失敗；以 <c>YMIR_UPDATE_OPENAPI=1</c> 執行可更新快照。
/// </summary>
public class OpenApiSnapshotTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [Fact]
    public async Task OpenApiDocument_MatchesCommittedSnapshot()
    {
        using var client = factory.CreateClient();
        var json = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);
        var normalized = JsonNode.Parse(json)!;
        normalized["servers"] = null; // 測試主機位址每次不同
        var actual = normalized.ToJsonString(Indented).ReplaceLineEndings("\n") + "\n";

        var path = Path.Combine(FindRepositoryRoot(), "src", "Ymir.Api", "openapi", "v1.json");
        if (Environment.GetEnvironmentVariable("YMIR_UPDATE_OPENAPI") == "1")
        {
            await File.WriteAllTextAsync(path, actual, TestContext.Current.CancellationToken);
            return;
        }

        Assert.True(File.Exists(path), $"找不到 {path}，請以 YMIR_UPDATE_OPENAPI=1 執行此測試產生快照。");
        var expected = (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");
        Assert.True(expected == actual, "API 契約已變更：請以 YMIR_UPDATE_OPENAPI=1 dotnet test 更新 src/Ymir.Api/openapi/v1.json，並在 web/ 執行 npm run api:generate。");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ymir.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
