using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ymir.Platform.Settings;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Extensions;

/// <summary>某位成員目前有效的擴充能力（ADR-0012 A.3）；啟動 Agent 時由伺服器據此組合 Pi 參數。</summary>
public sealed record EffectiveExtensions(bool Skills, bool Mcp)
{
    /// <summary>預設拒絕（ADR-0012 A.1）。</summary>
    public static readonly EffectiveExtensions None = new(false, false);

    public bool Allows(ExtensionCapability capability) => capability switch
    {
        ExtensionCapability.Skills => Skills,
        ExtensionCapability.Mcp => Mcp,
        _ => false,
    };
}

/// <summary>全域預設，存在 <c>platform.system_settings</c>（明文 JSON，不含機密，ADR-0010）。沒有設定時全部關閉。</summary>
public sealed record ExtensionPolicySettings(bool Skills, bool Mcp)
{
    public EffectiveExtensions ToEffective() => new(Skills, Mcp);
}

/// <param name="Stored">管理員儲存的值（沒有則為 null，全部關閉）。</param>
public sealed record ExtensionPolicyState(ExtensionPolicySettings Effective, SystemSettingValue? Stored);

/// <summary>成員的覆寫與有效值；<see cref="Grants"/> 沒有的能力表示繼承全域預設。</summary>
public sealed record UserExtensionState(IReadOnlyDictionary<ExtensionCapability, ExtensionGrantEffect> Grants, EffectiveExtensions Effective);

/// <summary>解析成員的有效擴充能力（ADR-0012 A.3）。</summary>
public interface IExtensionPolicy
{
    Task<EffectiveExtensions> ResolveAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// 擴充政策（ADR-0012 A.2）：全域預設 + 每位成員覆寫。全域預設資料庫優先、不必重啟，快取 30 秒（比照 ADR-0011 的執行政策）；
/// 覆寫每次直接查資料表（每次執行一次、資料量小）。資料庫暫時無法使用時沿用上次的全域值，不讓執行失敗；
/// 覆寫查詢失敗則拋出，由執行流程轉成失敗事件，不會因此放寬權限。
/// </summary>
public sealed partial class ExtensionPolicyService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ExtensionPolicyService> logger) : IExtensionPolicy, IDisposable
{
    public const string Key = "vibemaker.extension_policy";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly ExtensionPolicySettings Default = new(false, false);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private SystemSettingValue? _stored;
    private ExtensionPolicySettings? _storedSettings;
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;

    public async Task<ExtensionPolicyState> GetStateAsync(CancellationToken cancellationToken)
    {
        if (timeProvider.GetUtcNow() - _loadedAt >= CacheDuration)
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }

        return new ExtensionPolicyState(_storedSettings ?? Default, _stored);
    }

    public async Task<ExtensionPolicyState> RefreshAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var stored = await scope.ServiceProvider.GetRequiredService<ISystemSettingsStore>().GetAsync(Key, cancellationToken).ConfigureAwait(false);
                var settings = stored is null ? null : Parse(stored.Value);
                // 內容損毀時忽略，退回預設（全部關閉）。
                _stored = settings is null ? null : stored;
                _storedSettings = settings;
            }
        }
#pragma warning disable CA1031 // 資料庫暫時無法使用時沿用上次的值，不讓 execution 失敗。
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

        return new ExtensionPolicyState(_storedSettings ?? Default, _stored);
    }

    public async Task<ExtensionPolicyState> SaveAsync(ExtensionPolicySettings settings, string updatedBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            await scope.ServiceProvider.GetRequiredService<ISystemSettingsStore>()
                .SetAsync(Key, JsonSerializer.Serialize(settings, JsonOptions), updatedBy, cancellationToken).ConfigureAwait(false);
        }

        return await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<EffectiveExtensions> ResolveAsync(Guid userId, CancellationToken cancellationToken) =>
        (await GetUserStateAsync(userId, cancellationToken).ConfigureAwait(false)).Effective;

    public async Task<UserExtensionState> GetUserStateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var global = (await GetStateAsync(cancellationToken).ConfigureAwait(false)).Effective;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var grants = await scope.ServiceProvider.GetRequiredService<IVibeMakerDbContext>().UserExtensionGrants.AsNoTracking()
                .Where(g => g.UserId == userId)
                .ToDictionaryAsync(g => g.Capability, g => g.Effect, cancellationToken).ConfigureAwait(false);
            return new UserExtensionState(grants, Combine(global, grants));
        }
    }

    /// <summary>設定成員的覆寫；值為 null 表示刪除覆寫（改為繼承全域預設）。</summary>
    public async Task<UserExtensionState> SetUserGrantsAsync(
        Guid userId,
        IReadOnlyDictionary<ExtensionCapability, ExtensionGrantEffect?> grants,
        string updatedBy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grants);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var db = scope.ServiceProvider.GetRequiredService<IVibeMakerDbContext>();
            var existing = await db.UserExtensionGrants.Where(g => g.UserId == userId).ToListAsync(cancellationToken).ConfigureAwait(false);
            var now = timeProvider.GetUtcNow();
            foreach (var (capability, effect) in grants)
            {
                var current = existing.SingleOrDefault(g => g.Capability == capability);
                if (effect is null)
                {
                    if (current is not null)
                    {
                        db.UserExtensionGrants.Remove(current);
                    }
                }
                else if (current is null)
                {
                    db.UserExtensionGrants.Add(UserExtensionGrant.Create(userId, capability, effect.Value, updatedBy, now));
                }
                else if (current.Effect != effect.Value)
                {
                    current.Set(effect.Value, updatedBy, now);
                }
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return await GetUserStateAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    internal static EffectiveExtensions Combine(ExtensionPolicySettings global, IReadOnlyDictionary<ExtensionCapability, ExtensionGrantEffect> grants)
    {
        bool Resolve(ExtensionCapability capability, bool fallback) =>
            grants.TryGetValue(capability, out var effect) ? effect == ExtensionGrantEffect.Allow : fallback;

        return new EffectiveExtensions(Resolve(ExtensionCapability.Skills, global.Skills), Resolve(ExtensionCapability.Mcp, global.Mcp));
    }

    internal static ExtensionPolicySettings? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ExtensionPolicySettings>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Dispose() => _lock.Dispose();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to load the extension policy setting; keeping the previous value")]
    private static partial void LogLoadFailed(ILogger logger, Exception exception);
}
