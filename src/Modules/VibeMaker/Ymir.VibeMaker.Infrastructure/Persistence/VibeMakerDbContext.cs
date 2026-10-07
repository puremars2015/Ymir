using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Infrastructure.Persistence;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Infrastructure.Persistence;

/// <summary>Vibe Maker 模組的資料（schema <c>vibemaker</c>）。跨模組只存 user_id，不建 FK（ADR-0001）。</summary>
public sealed class VibeMakerDbContext(DbContextOptions<VibeMakerDbContext> options) : DbContext(options), IVibeMakerDbContext
{
    public const string Schema = "vibemaker";

    public DbSet<ExecutionArtifact> ExecutionArtifacts => Set<ExecutionArtifact>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<AgentSession> AgentSessions => Set<AgentSession>();

    public DbSet<AgentRuntimeRecord> AgentRuntimes => Set<AgentRuntimeRecord>();

    public DbSet<AgentExecution> AgentExecutions => Set<AgentExecution>();

    public DbSet<ExecutionEventRecord> ExecutionEvents => Set<ExecutionEventRecord>();

    public DbSet<UserSettings> UserSettings => Set<UserSettings>();

    public DbSet<MakeTopic> MakeTopics => Set<MakeTopic>();

    public DbSet<MessageAttachment> MessageAttachments => Set<MessageAttachment>();

    public DbSet<UserExtensionGrant> UserExtensionGrants => Set<UserExtensionGrant>();

    public DbSet<OneDriveConnection> OneDriveConnections => Set<OneDriveConnection>();

    public DbSet<OneDriveSyncScope> OneDriveSyncScopes => Set<OneDriveSyncScope>();

    public DbSet<OneDriveSyncItem> OneDriveSyncItems => Set<OneDriveSyncItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<ExecutionArtifact>(artifact =>
        {
            artifact.ToTable("execution_artifacts");
            artifact.HasKey(a => a.Id);
            artifact.Property(a => a.Id).ValueGeneratedNever();
            artifact.Property(a => a.Path).HasMaxLength(1024).IsRequired();
            artifact.HasIndex(a => a.ExecutionId);
            artifact.HasOne<AgentExecution>().WithMany().HasForeignKey(a => a.ExecutionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Project>(project =>
        {
            project.ToTable("projects");
            project.HasKey(p => p.Id);
            project.Property(p => p.Id).ValueGeneratedNever();
            project.Property(p => p.Name).HasMaxLength(Project.NameMaxLength).IsRequired();
            project.Property(p => p.SystemPrompt).HasMaxLength(Project.SystemPromptMaxLength);
            project.Property(p => p.Status).HasConversion<UpperSnakeCaseEnumConverter<ProjectStatus>>().HasMaxLength(30);
            project.HasIndex(p => p.UserId);
        });

        modelBuilder.Entity<Conversation>(conversation =>
        {
            conversation.ToTable("conversations");
            conversation.HasKey(c => c.Id);
            conversation.Property(c => c.Id).ValueGeneratedNever();
            conversation.Property(c => c.Title).HasMaxLength(Conversation.TitleMaxLength).IsRequired();
            conversation.Property(c => c.ModelId).HasMaxLength(Conversation.ModelIdMaxLength);
            conversation.Property(c => c.Status).HasConversion<UpperSnakeCaseEnumConverter<ConversationStatus>>().HasMaxLength(30);
            conversation.HasOne<Project>().WithMany().HasForeignKey(c => c.ProjectId).OnDelete(DeleteBehavior.Restrict);
            conversation.HasIndex(c => new { c.UserId, c.ProjectId });
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
            // 一個使用者最多一個未刪除的 runtime（ADR-0007），由資料庫保證。跨模組只存 user_id，不建 FK（ADR-0001）。
            runtime.HasIndex(r => r.UserId).IsUnique().HasFilter("[status] <> 'DELETED'");
        });

        modelBuilder.Entity<AgentExecution>(execution =>
        {
            execution.ToTable("agent_executions");
            execution.HasKey(e => e.Id);
            execution.Property(e => e.Id).ValueGeneratedNever();
            execution.Property(e => e.Status).HasConversion<UpperSnakeCaseEnumConverter<ExecutionStatus>>().HasMaxLength(30);
            execution.Property(e => e.ErrorCode).HasMaxLength(100);
            execution.Property(e => e.ModelId).HasMaxLength(Conversation.ModelIdMaxLength);
            execution.Property(e => e.AgentPrompt);
            execution.Property(e => e.RowVersion).IsRowVersion();
            execution.HasOne<Conversation>().WithMany().HasForeignKey(e => e.ConversationId).OnDelete(DeleteBehavior.Restrict);
            execution.HasOne<Message>().WithMany().HasForeignKey(e => e.UserMessageId).OnDelete(DeleteBehavior.Restrict);
            // 同一 Conversation 同時只能有一個 QUEUED / RUNNING（SA §14），由資料庫保證而不是先查再寫。
            execution.HasIndex(e => e.ConversationId).IsUnique().HasFilter("[status] IN ('QUEUED', 'RUNNING')")
                .HasDatabaseName("ux_agent_executions_active_per_conversation");
            // 前端重送不產生重複 execution（SA §9.1）。
            execution.HasIndex(e => new { e.UserId, e.ClientRequestId }).IsUnique();
            execution.HasIndex(e => e.Status);
            // Admin 總覽依建立時間統計近幾天的執行數（ADR-0010）
            execution.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<ExecutionEventRecord>(evt =>
        {
            evt.ToTable("execution_events");
            evt.HasKey(e => new { e.ExecutionId, e.Sequence });
            evt.Property(e => e.EventType).HasMaxLength(50).IsRequired();
            evt.Property(e => e.Data).IsRequired();
            evt.HasOne<AgentExecution>().WithMany().HasForeignKey(e => e.ExecutionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserSettings>(settings =>
        {
            settings.ToTable("user_settings");
            settings.HasKey(s => s.UserId);
            settings.Property(s => s.UserId).ValueGeneratedNever();
            settings.Property(s => s.SystemPrompt).HasMaxLength(Project.SystemPromptMaxLength);
        });

        modelBuilder.Entity<MakeTopic>(topic =>
        {
            topic.ToTable("make_topics");
            topic.HasKey(t => t.Id);
            topic.Property(t => t.Id).ValueGeneratedNever();
            topic.Property(t => t.Name).HasMaxLength(MakeTopic.NameMaxLength).IsRequired();
            topic.Property(t => t.Description).HasMaxLength(MakeTopic.DescriptionMaxLength);
            topic.Property(t => t.Instructions).HasMaxLength(MakeTopic.InstructionsMaxLength).IsRequired();
            topic.HasIndex(t => new { t.IsEnabled, t.SortOrder });
        });

        modelBuilder.Entity<MessageAttachment>(attachment =>
        {
            attachment.ToTable("message_attachments");
            attachment.HasKey(a => a.Id);
            attachment.Property(a => a.Id).ValueGeneratedNever();
            attachment.Property(a => a.FileName).HasMaxLength(MessageAttachment.FileNameMaxLength).IsRequired();
            attachment.Property(a => a.Path).HasMaxLength(MessageAttachment.PathMaxLength).IsRequired();
            attachment.Property(a => a.ContentType).HasMaxLength(MessageAttachment.ContentTypeMaxLength).IsRequired();
            attachment.HasOne<Conversation>().WithMany().HasForeignKey(a => a.ConversationId).OnDelete(DeleteBehavior.Restrict);
            attachment.HasOne<Message>().WithMany().HasForeignKey(a => a.MessageId).OnDelete(DeleteBehavior.Restrict);
            attachment.HasIndex(a => new { a.ConversationId, a.MessageId });
            attachment.HasIndex(a => a.MessageId);
        });

        modelBuilder.Entity<UserExtensionGrant>(grant =>
        {
            // ADR-0012 A.2：每位成員每種能力最多一筆覆寫；跨模組只存 user_id，不建 FK（ADR-0001）。
            grant.ToTable("user_extension_grants");
            grant.HasKey(g => new { g.UserId, g.Capability });
            grant.Property(g => g.Capability).HasConversion<UpperSnakeCaseEnumConverter<ExtensionCapability>>().HasMaxLength(30);
            grant.Property(g => g.Effect).HasConversion<UpperSnakeCaseEnumConverter<ExtensionGrantEffect>>().HasMaxLength(30);
            grant.Property(g => g.UpdatedBy).HasMaxLength(UserExtensionGrant.UpdatedByMaxLength).IsRequired();
        });

        modelBuilder.Entity<OneDriveConnection>(connection =>
        {
            // ADR-0013：一位使用者最多一個 OneDrive 連結；refresh token 只存 Data Protection 加密後的值。
            connection.ToTable("onedrive_connections");
            connection.HasKey(c => c.UserId);
            connection.Property(c => c.UserId).ValueGeneratedNever();
            connection.Property(c => c.MicrosoftUserId).HasMaxLength(OneDriveConnection.MicrosoftUserIdMaxLength).IsRequired();
            connection.Property(c => c.UserPrincipalName).HasMaxLength(OneDriveConnection.UserPrincipalNameMaxLength).IsRequired();
            connection.Property(c => c.DriveId).HasMaxLength(OneDriveConnection.DriveIdMaxLength).IsRequired();
            connection.Property(c => c.RootItemId).HasMaxLength(OneDriveConnection.ItemIdMaxLength);
            connection.Property(c => c.RootPath).HasMaxLength(OneDriveConnection.RootPathMaxLength);
            connection.Property(c => c.ProtectedRefreshToken).IsRequired();
            connection.Property(c => c.Status).HasConversion<UpperSnakeCaseEnumConverter<OneDriveConnectionStatus>>().HasMaxLength(30);
            connection.Property(c => c.LastError).HasMaxLength(OneDriveConnection.ErrorMaxLength);
        });

        modelBuilder.Entity<OneDriveSyncScope>(scope =>
        {
            // ADR-0013 §4：一個工作目錄（專案或未分組對話）一筆；upload_pending 是持久化的同步工作佇列。
            scope.ToTable("onedrive_sync_scopes");
            scope.HasKey(s => s.ScopeId);
            scope.Property(s => s.ScopeId).ValueGeneratedNever();
            scope.Property(s => s.DriveId).HasMaxLength(OneDriveConnection.DriveIdMaxLength).IsRequired();
            scope.Property(s => s.RootItemId).HasMaxLength(OneDriveConnection.ItemIdMaxLength).IsRequired();
            scope.Property(s => s.FolderItemId).HasMaxLength(OneDriveConnection.ItemIdMaxLength).IsRequired();
            scope.Property(s => s.FolderPath).HasMaxLength(OneDriveSyncScope.FolderNameMaxLength).IsRequired();
            scope.Property(s => s.State).HasConversion<UpperSnakeCaseEnumConverter<OneDriveSyncState>>().HasMaxLength(30);
            scope.Property(s => s.LastError).HasMaxLength(OneDriveConnection.ErrorMaxLength);
            scope.HasIndex(s => s.UserId);
            scope.HasIndex(s => new { s.UploadPending, s.NextAttemptAt });
        });

        modelBuilder.Entity<OneDriveSyncItem>(item =>
        {
            item.ToTable("onedrive_sync_items");
            item.HasKey(i => i.Id);
            item.Property(i => i.Id).ValueGeneratedNever();
            item.Property(i => i.Path).HasMaxLength(OneDriveSyncItem.PathMaxLength).IsRequired();
            item.Property(i => i.ItemId).HasMaxLength(OneDriveConnection.ItemIdMaxLength).IsRequired();
            item.Property(i => i.ETag).HasMaxLength(OneDriveConnection.ItemIdMaxLength);
            item.HasIndex(i => i.ScopeId);
            item.HasOne<OneDriveSyncScope>().WithMany().HasForeignKey(i => i.ScopeId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.ApplySnakeCaseNames();
    }
}
