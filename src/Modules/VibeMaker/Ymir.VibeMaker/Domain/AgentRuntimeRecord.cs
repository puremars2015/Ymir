namespace Ymir.VibeMaker.Domain;

/// <summary>
/// Runtime metadata（SA §8 AGENT_RUNTIME）。Container 是運算資源，不是唯一資料來源（SA §15）；
/// 每個 workspace 最多一筆未刪除的紀錄（filtered unique index）。
/// </summary>
public sealed class AgentRuntimeRecord
{
    private AgentRuntimeRecord()
    {
    }

    public Guid Id { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public string Provider { get; private set; } = string.Empty;

    public string ProviderRuntimeId { get; private set; } = string.Empty;

    public string ImageVersion { get; private set; } = string.Empty;

    public RuntimeStatus Status { get; private set; }

    public DateTimeOffset? LastActiveAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static AgentRuntimeRecord Create(Guid id, Guid workspaceId, string provider, string providerRuntimeId, string imageVersion, RuntimeStatus status, DateTimeOffset now) =>
        new()
        {
            Id = id,
            WorkspaceId = workspaceId,
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
    }
}
