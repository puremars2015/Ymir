namespace Ymir.VibeMaker.Domain;

/// <summary>
/// Runtime metadata（SA §8 AGENT_RUNTIME）。Container 是運算資源，不是唯一資料來源（SA §15）；
/// 每個使用者最多一筆未刪除的紀錄（filtered unique index，ADR-0007）。
/// </summary>
public sealed class AgentRuntimeRecord
{
    private AgentRuntimeRecord()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Provider { get; private set; } = string.Empty;

    public string ProviderRuntimeId { get; private set; } = string.Empty;

    public string ImageVersion { get; private set; } = string.Empty;

    public RuntimeStatus Status { get; private set; }

    public DateTimeOffset? LastActiveAt { get; private set; }

    /// <summary>
    /// 平台刻意停止（Admin 手動停止、帳號停用、管理員開啟的閒置停止）：API 重新啟動對帳時不自動啟動（ADR-0033）。
    /// 下一次 execution 啟動 runtime（<see cref="Update"/>）時清除。
    /// </summary>
    public bool StopRequested { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static AgentRuntimeRecord Create(Guid id, Guid userId, string provider, string providerRuntimeId, string imageVersion, RuntimeStatus status, DateTimeOffset now) =>
        new()
        {
            Id = id,
            UserId = userId,
            Provider = provider,
            ProviderRuntimeId = providerRuntimeId,
            ImageVersion = imageVersion,
            Status = status,
            LastActiveAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

    public void Update(string providerRuntimeId, string imageVersion, RuntimeStatus status, DateTimeOffset now)
    {
        ProviderRuntimeId = providerRuntimeId;
        ImageVersion = imageVersion;
        Status = status;
        LastActiveAt = now;
        UpdatedAt = now;
        StopRequested = false;
    }

    /// <summary>使用者活動（execution 結束）：管理員開啟閒置停止時以此計時（ADR-0011、ADR-0033）。</summary>
    public void MarkActive(DateTimeOffset now)
    {
        LastActiveAt = now;
        UpdatedAt = now;
    }

    /// <summary>只更新狀態（例如對帳），不視為使用者活動。</summary>
    public void MarkStatus(RuntimeStatus status, DateTimeOffset now)
    {
        Status = status;
        UpdatedAt = now;
    }

    /// <summary>平台刻意停止（Admin、帳號停用、閒置停止）：記錄下來，對帳時不自動啟動（ADR-0033）。</summary>
    public void MarkStopped(DateTimeOffset now)
    {
        Status = RuntimeStatus.Stopped;
        StopRequested = true;
        UpdatedAt = now;
    }
}
