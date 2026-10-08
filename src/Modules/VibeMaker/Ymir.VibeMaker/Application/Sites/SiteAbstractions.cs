using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Ymir.VibeMaker.Application.Sites;

/// <summary><c>Ymir:Sites</c>（ADR-0016）。沒有設定 <see cref="BaseUrl"/> 時網站託管停用。</summary>
public sealed class SiteOptions
{
    public const string SectionName = "Ymir:Sites";

    /// <summary>網站的基底網址，例如 <c>https://sites.example.com</c>；網站網址為 <c>{scheme}://{slug}.{host}[:port]/</c>。</summary>
    public Uri? BaseUrl { get; set; }

    /// <summary>網站 volume（API 可寫、SiteHost 唯讀）。</summary>
    public string? Root { get; set; }

    public long MaxBytes { get; set; } = 50L * 1024 * 1024;

    public int MaxFiles { get; set; } = 2000;

    public int MaxSitesPerUser { get; set; } = 20;

    public int KeepVersions { get; set; } = 3;

    public bool IsEnabled => BaseUrl is not null && !string.IsNullOrWhiteSpace(Root);

    /// <summary>網站網址（ADR-0016 §2）。</summary>
    public Uri UrlFor(string slug)
    {
        var baseUrl = BaseUrl ?? throw new InvalidOperationException("Ymir:Sites:BaseUrl is not configured.");
        return new UriBuilder(baseUrl.Scheme, $"{slug}.{baseUrl.Host}", baseUrl.IsDefaultPort ? -1 : baseUrl.Port, "/").Uri;
    }

    /// <summary>8 碼小寫英數字（去掉容易混淆的 0、1、l、o）。</summary>
    public static string NewSlug()
    {
        const string alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
        return string.Create(Domain.Site.SlugLength, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
            }
        });
    }
}

/// <summary>網站 volume 的檔案（路徑只由 site id、version id 與已檢查的相對路徑推導）。</summary>
public interface ISiteStorage
{
    Task WriteFileAsync(Guid siteId, Guid versionId, string relativePath, Stream content, CancellationToken cancellationToken);

    void DeleteVersion(Guid siteId, Guid versionId);

    void DeleteSite(Guid siteId);
}

/// <summary>同一個網站的發布序列化（ADR-0016 §2）。</summary>
public sealed class SiteLocks
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var semaphore = _locks.GetOrAdd(siteId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
