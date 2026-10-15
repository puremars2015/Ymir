using Microsoft.AspNetCore.Http;
using Ymir.Api.Infrastructure;
using Ymir.SiteHost;
using Ymir.VibeMaker.Application.Make;
using Ymir.VibeMaker.Domain;

namespace Ymir.UnitTests.Sites;

/// <summary>左側「小工具」（ADR-0029）：主題指示、自動發布的網站預設私人、iframe 相關的標頭與 cookie。</summary>
public class ToolRulesTests
{
    [Fact]
    public void ToolTopics_GetThePlatformPublishingInstruction()
    {
        var tool = MakePromptBuilder.ForTopic(new MakeTopicPrompt("小工具架設", null, "單一 HTML", ProducesTool: true));
        var site = MakePromptBuilder.ForTopic(new MakeTopicPrompt("網站系統架設", null, "前後端"));
        var described = MakePromptBuilder.ForDescription("計算機", [new MakeTopicPrompt("小工具架設", null, "單一 HTML", ProducesTool: true)]);

        Assert.Contains("tool/index.html", tool, StringComparison.Ordinal);
        Assert.Contains("tool/index.html", described, StringComparison.Ordinal);
        Assert.DoesNotContain("tool/index.html", site, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolSites_StartPrivate()
    {
        var now = DateTimeOffset.UtcNow;
        var tool = Site.Create(Guid.NewGuid(), Guid.NewGuid(), "abcdefgh", "計算機", Guid.NewGuid(), "tool", false, now, isTool: true);
        var site = Site.Create(Guid.NewGuid(), Guid.NewGuid(), "abcdefgh", "網站", Guid.NewGuid(), "dist", false, now);

        Assert.True(tool.IsTool);
        Assert.Equal(SiteAccessMode.SelectedUsers, tool.AccessMode);
        Assert.Equal(SiteAccessMode.Public, site.AccessMode);
    }

    [Theory]
    [InlineData(null, "default-src 'self'", "frame-src")]
    [InlineData("https://sites.example.com", "frame-src https://*.sites.example.com", null)]
    [InlineData("http://sites.localhost:5300", "frame-src http://*.sites.localhost:5300", null)]
    public void YmirCsp_FramesOnlyTheSiteDomain(string? baseUrl, string expected, string? absent)
    {
        var csp = SecurityHeaders.ContentSecurityPolicyFor(baseUrl is null ? null : new Uri(baseUrl));

        Assert.Contains(expected, csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        if (absent is not null)
        {
            Assert.DoesNotContain(absent, csp, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SiteHost_CanOnlyBeFramedByYmir()
    {
        Assert.Equal("frame-ancestors 'self' https://ymir.example.com", SiteHostApp.FrameAncestors(new Uri("https://ymir.example.com/app")));
        Assert.Equal("frame-ancestors 'self'", SiteHostApp.FrameAncestors(null));
    }

    [Theory]
    [InlineData("https://sites.example.com", true)]
    [InlineData("http://sites.localhost:5300", true)]
    [InlineData("http://sites.test", false)]
    public void SiteCookie_IsPartitionedCrossSiteOnlyInSecureContexts(string baseUrl, bool secure)
    {
        var cookie = SiteRequestHandler.CookieOptionsFor(new Uri(baseUrl), TimeSpan.FromHours(8));

        Assert.True(cookie.HttpOnly);
        Assert.Null(cookie.Domain);
        Assert.Equal(secure, cookie.Secure);
        Assert.Equal(secure ? SameSiteMode.None : SameSiteMode.Lax, cookie.SameSite);
        Assert.Equal(secure, cookie.Extensions.Contains("Partitioned"));
    }
}
