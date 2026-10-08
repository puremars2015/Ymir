using Microsoft.Extensions.Caching.Memory;
using Ymir.SiteHost;
using Ymir.VibeMaker.Application.Sites;
using Ymir.VibeMaker.Domain;
using Ymir.VibeMaker.Infrastructure.Sites;

namespace Ymir.UnitTests.Sites;

/// <summary>ADR-0016：網址、來源目錄、Host 對應與檔案解析的規則。</summary>
public sealed class SiteRulesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ymir-site-unit-" + Guid.NewGuid().ToString("N"));
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    private SiteRequestHandler Handler() =>
        new(new SiteHostOptions { BaseUrl = new Uri("https://sites.example.com"), Root = _root }, null!, null!, new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider(), _cache, TimeProvider.System);

    [Theory]
    [InlineData(SiteAccessMode.Public, false, false, false, true)]
    [InlineData(SiteAccessMode.AllUsers, false, false, false, true)]
    [InlineData(SiteAccessMode.SelectedUsers, false, false, false, false)]
    [InlineData(SiteAccessMode.SelectedUsers, false, false, true, true)]
    [InlineData(SiteAccessMode.SelectedUsers, true, false, false, true)]
    [InlineData(SiteAccessMode.SelectedUsers, false, true, false, true)]
    public void CanView_OwnersAndAdminsAlwaysCan_OthersFollowTheMode(SiteAccessMode mode, bool isOwner, bool isAdmin, bool isShared, bool expected)
    {
        var owner = Guid.NewGuid();
        var viewer = isOwner ? owner : Guid.NewGuid();
        Assert.Equal(expected, SiteAccessRules.CanView(mode, owner, viewer, isAdmin, isShared));
    }

    [Theory]
    [InlineData("/orders/1?x=1", "/orders/1?x=1")]
    [InlineData("/", "/")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("//evil.example/", "/")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("/a\r\nSet-Cookie: x", "/")]
    public void SafeReturnPath_OnlyAllowsSiteRelativePaths(string? path, string expected) =>
        Assert.Equal(expected, SiteAccessRules.SafeReturnPath(path));

    [Fact]
    public void TicketHash_IsStableAndDoesNotContainTheTicket()
    {
        var hash = SiteAccessRules.HashTicket("abc");
        Assert.Equal(hash, SiteAccessRules.HashTicket("abc"));
        Assert.Equal(64, hash.Length);
        Assert.NotEqual(hash, SiteAccessRules.HashTicket("abd"));
    }

    [Fact]
    public void Urls_UseTheSlugAsSubdomain_AndKeepScheme()
    {
        var options = new SiteOptions { BaseUrl = new Uri("https://sites.example.com"), Root = "/x" };
        Assert.Equal("https://abcd2345.sites.example.com/", options.UrlFor("abcd2345").ToString());
        Assert.Equal("http://abcd2345.sites.localhost:5300/", new SiteOptions { BaseUrl = new Uri("http://sites.localhost:5300") }.UrlFor("abcd2345").ToString());
        var slug = SiteOptions.NewSlug();
        Assert.Matches("^[a-z2-9]{8}$", slug);
    }

    [Theory]
    [InlineData(null, ".")]
    [InlineData(" / ", ".")]
    [InlineData("dist", "dist")]
    [InlineData("/web/dist/", "web/dist")]
    [InlineData("../etc", null)]
    [InlineData(".git", null)]
    [InlineData("a/node_modules", null)]
    public void SourcePaths_AreRelativeAndSafe(string? input, string? expected)
    {
        Assert.Equal(expected, SiteService.NormalizeSource(input));
    }

    [Theory]
    [InlineData("abcd2345.sites.example.com", "abcd2345")]
    [InlineData("ABCD2345.Sites.Example.com", "abcd2345")]
    [InlineData("sites.example.com", null)]
    [InlineData("x.abcd2345.sites.example.com", null)]
    [InlineData("abcd2345.sites.example.com.evil.test", null)]
    [InlineData("abc-2345.sites.example.com", null)]
    public void Hosts_MapToSlugs(string host, string? expected)
    {
        Assert.Equal(expected, Handler().SlugOf(host));
    }

    [Fact]
    public void Files_ResolveInsideTheVersionDirectory_WithSpaFallback()
    {
        var site = new SiteInfo(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), SpaMode: true, SiteAccessMode.Public);
        var directory = FileSystemSiteStorage.VersionDirectory(_root, site.Id, site.VersionId);
        Directory.CreateDirectory(Path.Combine(directory, "docs"));
        File.WriteAllText(Path.Combine(directory, "index.html"), "root");
        File.WriteAllText(Path.Combine(directory, "docs", "index.html"), "docs");
        File.WriteAllText(Path.Combine(_root, "outside.html"), "secret");
        var handler = Handler();

        Assert.Equal(Path.Combine(directory, "index.html"), handler.Resolve(site, "/"));
        Assert.Equal(Path.Combine(directory, "docs", "index.html"), handler.Resolve(site, "/docs/"));
        Assert.Equal(Path.Combine(directory, "docs", "index.html"), handler.Resolve(site, "/docs"));
        Assert.Equal(Path.Combine(directory, "index.html"), handler.Resolve(site, "/settings/profile"));
        Assert.Null(handler.Resolve(site, "/missing.css"));
        Assert.Null(handler.Resolve(site, "/../../outside.html"));
        Assert.Null(handler.Resolve(site with { SpaMode = false }, "/settings/profile"));
    }

    public void Dispose()
    {
        _cache.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
