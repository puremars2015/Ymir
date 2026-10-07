using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Infrastructure.LiteLlm;
using Ymir.VibeMaker.Infrastructure.Persistence;

namespace Ymir.VibeMaker.Infrastructure.Health;

/// <summary>runtime manager 自我檢查（runtime host 是否可連線、container CLI 是否可用）。</summary>
internal interface IRuntimeAvailability
{
    /// <returns>可用時為 null，否則為給管理員看的簡短原因（不含路徑或 token）。</returns>
    Task<string?> CheckAvailabilityAsync(CancellationToken cancellationToken);
}

/// <summary>
/// readiness 檢查（tag <see cref="ReadyTag"/>）：資料庫、執行環境、LiteLLM。結果顯示在 <c>/health</c> 與管理總覽。
/// 描述只寫給管理員看的摘要，例外細節只進 server log（SA §12）。
/// </summary>
internal static class VibeMakerHealthChecks
{
    public const string ReadyTag = "ready";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
}

internal sealed class DatabaseHealthCheck(VibeMakerDbContext db, ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false)
                ? HealthCheckResult.Healthy("SQL Server 可連線")
                : HealthCheckResult.Unhealthy("無法連線到 SQL Server");
        }
#pragma warning disable CA1031 // 健康檢查失敗要回報狀態，不能讓例外中斷整個檢查。
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogWarning(ex, "Database health check failed");
            return HealthCheckResult.Unhealthy("無法連線到 SQL Server");
        }
    }
}

internal sealed class RuntimeHealthCheck(IAgentRuntimeManager runtimes, ILogger<RuntimeHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (runtimes is not IRuntimeAvailability availability)
        {
            return HealthCheckResult.Healthy();
        }

        try
        {
            var problem = await availability.CheckAvailabilityAsync(cancellationToken).ConfigureAwait(false);
            return problem is null ? HealthCheckResult.Healthy(Describe(runtimes)) : HealthCheckResult.Unhealthy(problem);
        }
#pragma warning disable CA1031 // 同上。
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogWarning(ex, "Runtime health check failed");
            return HealthCheckResult.Unhealthy("執行環境無法使用");
        }
    }

    private static string Describe(IAgentRuntimeManager runtimes) => runtimes switch
    {
        Runtime.Remote.RemoteRuntimeManager => "runtime host 可連線",
        Runtime.LocalRuntimeManager => "本機執行（開發用，無隔離）",
        _ => "container 引擎可用",
    };
}

/// <summary>LiteLLM proxy 是否存活（<c>/health/liveliness</c>，不需要金鑰）。沒有設定 LiteLLM（開發）時視為正常。</summary>
internal sealed class LiteLlmHealthCheck(IHttpClientFactory httpClients, IOptions<LiteLlmOptions> options, ILogger<LiteLlmHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.IsConfigured || settings.BaseUrl is null)
        {
            return HealthCheckResult.Healthy("未設定 LiteLLM（開發用）");
        }

        try
        {
            using var client = httpClients.CreateClient(nameof(LiteLlmHealthCheck));
            client.Timeout = VibeMakerHealthChecks.Timeout;
            using var response = await client.GetAsync(new Uri(settings.BaseUrl, "health/liveliness"), cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("LiteLLM 可連線")
                : HealthCheckResult.Unhealthy($"LiteLLM 回應 {(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "LiteLLM health check failed");
            return HealthCheckResult.Unhealthy("無法連線到 LiteLLM");
        }
    }
}
