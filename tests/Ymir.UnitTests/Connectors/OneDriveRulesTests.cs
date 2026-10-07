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
}
