using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Infrastructure.Persistence;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Infrastructure.Persistence;

/// <summary>Vibe Maker 模組的資料（schema <c>vibemaker</c>）。跨模組只存 user_id，不建 FK（ADR-0001）。</summary>
public sealed class VibeMakerDbContext(DbContextOptions<VibeMakerDbContext> options) : DbContext(options), IVibeMakerDbContext
{
    public const string Schema = "vibemaker";

    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<AgentSession> AgentSessions => Set<AgentSession>();

    public DbSet<AgentRuntimeRecord> AgentRuntimes => Set<AgentRuntimeRecord>();

    public DbSet<AgentExecution> AgentExecutions => Set<AgentExecution>();

    public DbSet<ExecutionEventRecord> ExecutionEvents => Set<ExecutionEventRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Workspace>(workspace =>
        {
            workspace.ToTable("workspaces");
            workspace.HasKey(w => w.Id);
            workspace.Property(w => w.Id).ValueGeneratedNever();
            workspace.Property(w => w.Name).HasMaxLength(Workspace.NameMaxLength).IsRequired();
            workspace.Property(w => w.StorageKey).HasMaxLength(500).IsRequired();
            workspace.Property(w => w.Status).HasConversion<UpperSnakeCaseEnumConverter<WorkspaceStatus>>().HasMaxLength(30);
            workspace.HasIndex(w => w.UserId);
        });

        modelBuilder.Entity<Conversation>(conversation =>
        {
            conversation.ToTable("conversations");
            conversation.HasKey(c => c.Id);
            conversation.Property(c => c.Id).ValueGeneratedNever();
            conversation.Property(c => c.Title).HasMaxLength(Conversation.TitleMaxLength).IsRequired();
            conversation.Property(c => c.Status).HasConversion<UpperSnakeCaseEnumConverter<ConversationStatus>>().HasMaxLength(30);
            conversation.HasOne<Workspace>().WithMany().HasForeignKey(c => c.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
            conversation.HasIndex(c => new { c.UserId, c.WorkspaceId });
        });

        modelBuilder.Entity<Message>(message =>
        {
            message.ToTable("messages");
            message.HasKey(m => m.Id);
            message.Property(m => m.Id).ValueGeneratedNever();
            message.Property(m => m.Content).IsRequired();
            message.Property(m => m.Role).HasConversion<UpperSnakeCaseEnumConverter<MessageRole>>().HasMaxLength(30);
            message.Property(m => m.MessageType).HasConversion<UpperSnakeCaseEnumConverter<MessageType>>().HasMaxLength(50);
            message.HasOne<Conversation>().WithMany().HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Restrict);
            message.HasIndex(m => new { m.ConversationId, m.SequenceNo }).IsUnique();
        });

        modelBuilder.Entity<AgentSession>(session =>
        {
            session.ToTable("agent_sessions");
            session.HasKey(s => s.Id);
            session.Property(s => s.Id).ValueGeneratedNever();
            session.Property(s => s.Provider).HasMaxLength(50).IsRequired();
            session.Property(s => s.ProviderSessionId).HasMaxLength(300);
            session.Property(s => s.Status).HasConversion<UpperSnakeCaseEnumConverter<AgentSessionStatus>>().HasMaxLength(30);
            session.HasOne<Conversation>().WithMany().HasForeignKey(s => s.ConversationId).OnDelete(DeleteBehavior.Restrict);
            session.HasIndex(s => s.ConversationId).IsUnique();
        });

        modelBuilder.Entity<AgentRuntimeRecord>(runtime =>
        {
            runtime.ToTable("agent_runtimes");
            runtime.HasKey(r => r.Id);
            runtime.Property(r => r.Id).ValueGeneratedNever();
            runtime.Property(r => r.Provider).HasMaxLength(50).IsRequired();
            runtime.Property(r => r.ProviderRuntimeId).HasMaxLength(300).IsRequired();
            runtime.Property(r => r.ImageVersion).HasMaxLength(100).IsRequired();
            runtime.Property(r => r.Status).HasConversion<UpperSnakeCaseEnumConverter<RuntimeStatus>>().HasMaxLength(30);
            runtime.HasOne<Workspace>().WithMany().HasForeignKey(r => r.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
            // MVP 每個 workspace 最多一個未刪除的 runtime（SA §8）。
            runtime.HasIndex(r => r.WorkspaceId).IsUnique().HasFilter("[status] <> 'DELETED'");
        });

        modelBuilder.Entity<AgentExecution>(execution =>
        {
            execution.ToTable("agent_executions");
            execution.HasKey(e => e.Id);
            execution.Property(e => e.Id).ValueGeneratedNever();
            execution.Property(e => e.Status).HasConversion<UpperSnakeCaseEnumConverter<ExecutionStatus>>().HasMaxLength(30);
            execution.Property(e => e.ErrorCode).HasMaxLength(100);
            execution.Property(e => e.RowVersion).IsRowVersion();
            execution.HasOne<Conversation>().WithMany().HasForeignKey(e => e.ConversationId).OnDelete(DeleteBehavior.Restrict);
            execution.HasOne<Message>().WithMany().HasForeignKey(e => e.UserMessageId).OnDelete(DeleteBehavior.Restrict);
            // 同一 Conversation 同時只能有一個 QUEUED / RUNNING（SA §14），由資料庫保證而不是先查再寫。
            execution.HasIndex(e => e.ConversationId).IsUnique().HasFilter("[status] IN ('QUEUED', 'RUNNING')")
                .HasDatabaseName("ux_agent_executions_active_per_conversation");
            // 前端重送不產生重複 execution（SA §9.1）。
            execution.HasIndex(e => new { e.UserId, e.ClientRequestId }).IsUnique();
            execution.HasIndex(e => e.Status);
        });

        modelBuilder.Entity<ExecutionEventRecord>(evt =>
        {
            evt.ToTable("execution_events");
            evt.HasKey(e => new { e.ExecutionId, e.Sequence });
            evt.Property(e => e.EventType).HasMaxLength(50).IsRequired();
            evt.Property(e => e.Data).IsRequired();
            evt.HasOne<AgentExecution>().WithMany().HasForeignKey(e => e.ExecutionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.ApplySnakeCaseNames();
    }
}
