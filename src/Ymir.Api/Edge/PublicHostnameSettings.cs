using Ymir.Edge;
using Ymir.Platform.Settings;

namespace Ymir.Api.Edge;

/// <summary>
/// 管理介面設定的對外網域（ADR-0010，key <see cref="Key"/>）。每個請求都會讀，所以快取 30 秒；
/// 本實例存檔後立即刷新，其他實例在快取到期後讀到新值。資料庫暫時無法使用時沿用上次的值。
/// </summary>
public sealed partial class PublicHostnameSettings(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<PublicHostnameSettings> logger)
    : IPublicHostnameSource, IDisposable
{
    public const string Key = "edge.public_hostname";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private SystemSettingValue? _current;
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;

    public async ValueTask<string?> GetOverrideAsync(CancellationToken cancellationToken) =>
        (await GetAsync(cancellationToken).ConfigureAwait(false))?.Value;

    /// <summary>目前的覆寫值（含更新時間與更新者），沒有則為 null。</summary>
    public async Task<SystemSettingValue?> GetAsync(CancellationToken cancellationToken)
    {
        if (timeProvider.GetUtcNow() - _loadedAt < CacheDuration)
        {
            return _current;
        }

        return await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<SystemSettingValue?> RefreshAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var stored = await scope.ServiceProvider.GetRequiredService<ISystemSettingsStore>().GetAsync(Key, cancellationToken).ConfigureAwait(false);
                // 資料庫裡的值不合法（例如手動改壞）時忽略，退回部署設定，避免整個站台被 Host 限制擋住。
                _current = stored is not null && PublicEdgeHostnames.IsValid(stored.Value) ? stored : null;
            }
        }
#pragma warning disable CA1031 // 資料庫暫時無法使用時沿用上次的值，不讓所有請求失敗。
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogLoadFailed(logger, ex);
        }
        finally
        {
            _loadedAt = timeProvider.GetUtcNow();
            _lock.Release();
        }

        return _current;
    }

    public void Dispose() => _lock.Dispose();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to load the public hostname setting; keeping the previous value")]
    private static partial void LogLoadFailed(ILogger logger, Exception exception);
}
