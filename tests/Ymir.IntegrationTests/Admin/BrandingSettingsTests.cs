using System.Net;
using System.Net.Http.Json;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.Platform.Users;

namespace Ymir.IntegrationTests.Admin;

/// <summary>公司識別（公司名稱 + favicon）：Ymir 專案名稱固定，公司名稱由 Admin 設定；讀取端點匿名。</summary>
public class BrandingSettingsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ByteArrayContent Octet(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("application/octet-stream");
        return content;
    }

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    [Fact]
    public async Task CompanyNameAndFavicon_AreSavedAndServedAnonymously()
    {
        using var admin = await factory.LoginAsync($"brand-{Guid.NewGuid():N}", UserRole.Admin);
        using var anonymous = factory.CreateClient();

        using var saved = await admin.PutAsJsonAsync("/api/admin/settings/branding", new SaveBrandingRequest("  Acme  "), Ct);
        using var upload = await admin.PutAsync("/api/admin/settings/branding/favicon", Octet(Png), Ct);
        var branding = await anonymous.GetFromJsonAsync<BrandingResponse>("/api/branding", JsonDefaults.Options, Ct);
        using var icon = await anonymous.GetAsync(branding!.FaviconUrl, Ct);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.Equal("Acme", branding.CompanyName);
        Assert.Equal("image/png", icon.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await icon.Content.ReadAsByteArrayAsync(Ct));

        using var reset = await admin.PutAsJsonAsync("/api/admin/settings/branding", new SaveBrandingRequest(""), Ct);
        using var removed = await admin.DeleteAsync("/api/admin/settings/branding/favicon", Ct);
        var after = await anonymous.GetFromJsonAsync<BrandingResponse>("/api/branding", JsonDefaults.Options, Ct);
        using var gone = await anonymous.GetAsync("/api/branding/favicon", Ct);

        Assert.Equal(string.Empty, after!.CompanyName);
        Assert.Null(after.FaviconUrl);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    // 一般多尺寸 .ico 常在 15～100 KB；系統設定原本只收 8000 字（約 6 KB 圖片），超過會回 500。
    [Theory]
    [InlineData(100 * 1024)]
    [InlineData(BrandingEndpoints.MaxFaviconBytes)]
    public async Task Favicon_UpToTheLimit_IsSavedAndServed(int size)
    {
        using var admin = await factory.LoginAsync($"brand-size-{Guid.NewGuid():N}", UserRole.Admin);
        using var anonymous = factory.CreateClient();
        var icon = new byte[size];
        Png.CopyTo(icon, 0);
        Random.Shared.NextBytes(icon.AsSpan(Png.Length));

        using var upload = await admin.PutAsync("/api/admin/settings/branding/favicon", Octet(icon), Ct);
        var branding = await upload.Content.ReadFromJsonAsync<BrandingResponse>(JsonDefaults.Options, Ct);
        using var served = await anonymous.GetAsync(branding!.FaviconUrl, Ct);

        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(icon, await served.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task Favicon_RejectsNonImagesAndOversizedFiles()
    {
        using var admin = await factory.LoginAsync($"brand-bad-{Guid.NewGuid():N}", UserRole.Admin);

        using var svg = await admin.PutAsync("/api/admin/settings/branding/favicon", Octet("<svg onload=alert(1)/>"u8.ToArray()), Ct);
        using var big = await admin.PutAsync("/api/admin/settings/branding/favicon", Octet([.. Png, .. new byte[BrandingEndpoints.MaxFaviconBytes]]), Ct);
        using var longName = await admin.PutAsJsonAsync("/api/admin/settings/branding", new SaveBrandingRequest(new string('x', 41)), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, svg.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, big.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, longName.StatusCode);
    }
}
