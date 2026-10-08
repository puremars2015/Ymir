using System.Net;
using Ymir.VibeMaker.Application.Connectors.OneDrive;
using Ymir.VibeMaker.Infrastructure.Connectors.OneDrive;

namespace Ymir.UnitTests.Connectors;

/// <summary>ADR-0013：根資料夾規則、Entra 端點推導、Graph 限流的重試間隔。</summary>
public class OneDriveRulesTests
{
    [Theory]
    [InlineData("/Ymir", "/Ymir")]
    [InlineData(" Ymir / Work ", "/Ymir/Work")]
    [InlineData("Ymir//專案", "/Ymir/專案")]
    public void Root_IsNormalized(string input, string expected)
    {
        Assert.Equal(expected, OneDrivePaths.NormalizeRoot(input).Path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("/a/../b")]
    [InlineData("/.")]
    [InlineData("/name.")]
    [InlineData("/a:b")]
    [InlineData("/a*b")]
    [InlineData("/a\u0001b")]
    [InlineData("/1/2/3/4/5/6")]
    public void Root_RejectsUnsafeOrInvalidNames(string? input)
    {
        var (path, problem) = OneDrivePaths.NormalizeRoot(input);

        Assert.Null(path);
        Assert.False(string.IsNullOrEmpty(problem));
    }

    [Theory]
    [InlineData("https://login.microsoftonline.com/tid/v2.0", "https://login.microsoftonline.com/tid/oauth2/v2.0/token")]
    [InlineData("http://127.0.0.1:5299/tid/v2.0", "http://127.0.0.1:5299/tid/oauth2/v2.0/token")]
    public void TokenEndpoint_IsDerivedFromAuthority(string authority, string expected)
    {
        Assert.Equal(expected, new OneDriveOAuthSettings(authority, "client", "secret").Endpoint("token").ToString());
    }

    [Fact]
    public void Settings_ToString_DoesNotLeakSecret()
    {
        Assert.DoesNotContain("super-secret", new OneDriveOAuthSettings("https://a/b/v2.0", "client", "super-secret").ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("token-value", new OneDriveTokens("token-value", "refresh-value", DateTimeOffset.UtcNow).ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(120, 30)]
    public void RetryAfter_IsHonoredUpToALimit(int seconds, int expected)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));

        Assert.Equal(TimeSpan.FromSeconds(expected), GraphOneDriveClient.RetryDelay(response));
    }

    [Fact]
    public void AuthorizeUri_UsesPkceAndOfflineAccess()
    {
        var client = new OneDriveOAuthClient(new HttpClient(), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<OneDriveOAuthClient>.Instance);

        var uri = client.BuildAuthorizeUri(
            new OneDriveOAuthSettings("https://login.microsoftonline.com/tid/v2.0", "client", "secret"),
            new Uri("https://ymir.example/api/connectors/onedrive/callback"),
            "state-1",
            "challenge-1",
            "alice@example.com").ToString();

        Assert.StartsWith("https://login.microsoftonline.com/tid/oauth2/v2.0/authorize?", uri, StringComparison.Ordinal);
        Assert.Contains("code_challenge=challenge-1", uri, StringComparison.Ordinal);
        Assert.Contains("code_challenge_method=S256", uri, StringComparison.Ordinal);
        Assert.Contains("offline_access", Uri.UnescapeDataString(uri), StringComparison.Ordinal);
        Assert.DoesNotContain("secret", uri, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("季報: 草稿", "季報_ 草稿-01234567")]
    [InlineData("  ", "未命名-01234567")]
    [InlineData("a/b\\c*?.", "a_b_c__-01234567")]
    public void FolderName_ReplacesCharactersOneDriveRejects_AndAppendsTheId(string name, string expected)
    {
        Assert.Equal(expected, OneDrivePaths.FolderName(name, Guid.Parse("01234567-89ab-cdef-0123-456789abcdef")));
    }

    [Fact]
    public void FolderName_IsTruncated()
    {
        var name = OneDrivePaths.FolderName(new string('x', 300), Guid.Empty);

        Assert.Equal(60 + 9, name.Length);
        Assert.True(OneDrivePaths.IsValidName(name));
    }

    [Theory]
    [InlineData("plan.md", "plan (OneDrive 衝突 20261007-093005).md")]
    [InlineData("src/app.min.js", "src/app.min (OneDrive 衝突 20261007-093005).js")]
    [InlineData("docs/README", "docs/README (OneDrive 衝突 20261007-093005)")]
    [InlineData(".gitignore", ".gitignore (OneDrive 衝突 20261007-093005)")]
    public void ConflictPath_KeepsTheDirectoryAndExtension(string path, string expected)
    {
        Assert.Equal(expected, OneDrivePaths.ConflictPath(path, new DateTimeOffset(2026, 10, 7, 9, 30, 5, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData("notes.txt", true)]
    [InlineData("src/中文/資料 1.bin", true)]
    [InlineData("bad:name.txt", false)]
    [InlineData("dir./a.txt", false)]
    [InlineData("trailing /a.txt", false)]
    [InlineData(".env", false)]
    [InlineData("node_modules/x.js", false)]
    [InlineData("../escape.txt", false)]
    public void SyncablePaths_FollowWorkspaceAndOneDriveRules(string path, bool expected)
    {
        Assert.Equal(expected, OneDrivePaths.IsSyncablePath(path));
    }

    [Fact]
    public void FailedSyncJobs_BackOff_ThenStop()
    {
        var now = DateTimeOffset.UnixEpoch;
        var scope = Ymir.VibeMaker.Domain.OneDriveSyncScope.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        scope.Enqueue(scope.ConversationId, now);

        scope.AttemptFailed("x", retry: true, now);
        Assert.Equal(now.AddMinutes(1), scope.NextAttemptAt);
        Assert.True(scope.UploadPending);
        for (var i = 1; i < Ymir.VibeMaker.Domain.OneDriveSyncScope.MaxAttempts; i++)
        {
            scope.AttemptFailed("x", retry: true, now);
        }

        Assert.False(scope.UploadPending);
        Assert.Equal(Ymir.VibeMaker.Domain.OneDriveSyncState.Failed, scope.State);

        // 手動重試重新開始計算；授權失效則不再自動重試。
        scope.Enqueue(scope.ConversationId, now);
        Assert.Equal(0, scope.Attempts);
        scope.AttemptFailed("請重新連結", retry: false, now);
        Assert.False(scope.UploadPending);
    }
}
