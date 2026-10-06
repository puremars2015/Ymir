using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Persistence;

/// <summary>
/// Application 層使用的資料存取介面。直接暴露 EF Core 的 <see cref="DbSet{TEntity}"/>（不另包 repository），
/// 實作與 SQL Server 細節在 Infrastructure（ADR-0001）。
/// </summary>
public interface IVibeMakerDbContext
{
    DbSet<Project> Projects { get; }

    DbSet<Conversation> Conversations { get; }

    DbSet<Message> Messages { get; }

    DbSet<AgentSession> AgentSessions { get; }

    DbSet<AgentRuntimeRecord> AgentRuntimes { get; }

    DbSet<AgentExecution> AgentExecutions { get; }

    DbSet<ExecutionEventRecord> ExecutionEvents { get; }

    DbSet<UserSettings> UserSettings { get; }

    DbSet<MakeTopic> MakeTopics { get; }

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
