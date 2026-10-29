using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Infrastructure.Persistence;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Infrastructure.Persistence;

/// <summary>Vibe Maker 模組的資料（schema <c>vibemaker</c>）。跨模組只存 user_id，不建 FK（ADR-0001）。</summary>
public sealed class VibeMakerDbContext(DbContextOptions<VibeMakerDbContext> options) : DbContext(options), IVibeMakerDbContext
{
    public const string Schema = "vibemaker";
    public DbSet<PersonalMcpServer> PersonalMcpServers => Set<PersonalMcpServer>();

    public DbSet<ExecutionArtifact> ExecutionArtifacts => Set<ExecutionArtifact>();

    public DbSet<ScheduledTask> ScheduledTasks => Set<ScheduledTask>();

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

    public DbSet<OneDriveLinkedItem> OneDriveLinkedItems => Set<OneDriveLinkedItem>();
    public DbSet<OneDriveLinkedFile> OneDriveLinkedFiles => Set<OneDriveLinkedFile>();

    public DbSet<McpServerAccess> McpServerAccess => Set<McpServerAccess>();

    public DbSet<UserPluginSetting> UserPluginSettings => Set<UserPluginSetting>();

    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();

    public DbSet<Site> Sites => Set<Site>();

    public DbSet<SiteVersion> SiteVersions => Set<SiteVersion>();

    public DbSet<SiteShare> SiteShares => Set<SiteShare>();

    public DbSet<SiteTicket> SiteTickets => Set<SiteTicket>();

    public Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginSerializableTransactionAsync(CancellationToken cancellationToken) =>
        Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<PersonalMcpServer>(server =>
        {
            server.ToTable("personal_mcp_servers");
            server.HasKey(s => s.Id);
            server.Property(s => s.Id).ValueGeneratedNever();
            server.Property(s => s.Name).HasMaxLength(40).IsRequired();
            server.Property(s => s.Url).HasMaxLength(2048).IsRequired();
            server.Property(s => s.ProtectedToken).HasMaxLength(8192);
            server.HasIndex(s => new { s.UserId, s.Name }).IsUnique();
        });
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
            execution.Property(e => e.ThinkingLevel).HasMaxLength(10);
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
            connection.Property(c => c.ProtectedRefreshToken).IsRequired();
            connection.Property(c => c.Status).HasConversion<UpperSnakeCaseEnumConverter<OneDriveConnectionStatus>>().HasMaxLength(30);
            connection.Property(c => c.LastError).HasMaxLength(OneDriveConnection.ErrorMaxLength);
        });

        modelBuilder.Entity<OneDriveLinkedItem>(link =>
        {
            link.ToTable("onedrive_linked_items");
            link.HasKey(l => l.Id);
            link.Property(l => l.Id).ValueGeneratedNever();
            link.Property(l => l.DriveId).HasMaxLength(OneDriveConnection.DriveIdMaxLength);
            link.Property(l => l.ItemId).HasMaxLength(OneDriveConnection.ItemIdMaxLength);
            link.Property(l => l.Name).HasMaxLength(255);
            link.Property(l => l.LocalName).HasMaxLength(200);
            link.Property(l => l.State).HasConversion<UpperSnakeCaseEnumConverter<OneDriveLinkedItemState>>().HasMaxLength(30);
            link.Property(l => l.LastError).HasMaxLength(OneDriveConnection.ErrorMaxLength);
            link.HasIndex(l => new { l.UserId, l.ScopeId, l.DriveId, l.ItemId }).IsUnique();
        });
        modelBuilder.Entity<OneDriveLinkedFile>(file =>
        {
            file.ToTable("onedrive_linked_files");
            file.HasKey(f => f.Id);
            file.Property(f => f.Id).ValueGeneratedNever();
            file.Property(f => f.Path).HasMaxLength(OneDriveLinkedFile.PathMaxLength);
            file.Property(f => f.PathKey).HasMaxLength(64).IsUnicode(false);
            file.Property(f => f.ItemId).HasMaxLength(OneDriveConnection.ItemIdMaxLength);
            file.Property(f => f.ETag).HasMaxLength(OneDriveConnection.ItemIdMaxLength);
            file.Property(f => f.LocalHash).HasMaxLength(64);
            file.HasIndex(f => new { f.LinkId, f.PathKey }).IsUnique();
            file.HasOne<OneDriveLinkedItem>().WithMany().HasForeignKey(f => f.LinkId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<McpServerAccess>(access =>
        {
            // ADR-0012 B.4：平台 MCP 服務的存取清單；服務本身只在版控的目錄中定義。
            access.ToTable("mcp_server_access");
            access.HasKey(a => a.ServerName);
            access.Property(a => a.ServerName).HasMaxLength(40);
            access.Property(a => a.Mode).HasConversion<UpperSnakeCaseEnumConverter<McpAccessMode>>().HasMaxLength(30);
            access.Property(a => a.UserIdList).IsRequired();
            access.Ignore(a => a.UserIds);
        });

        modelBuilder.Entity<UserPluginSetting>(setting =>
        {
            // ADR-0030 §3：使用者自己的外掛開關；沒有紀錄時用外掛的預設值。
            setting.ToTable("user_plugin_settings");
            setting.HasKey(s => new { s.UserId, s.PluginKey });
            setting.Property(s => s.PluginKey).HasMaxLength(UserPluginSetting.KeyMaxLength);
        });

        modelBuilder.Entity<KnowledgeDocument>(document =>
        {
            // ADR-0014：專案知識庫的文件版本；原始檔與向量在 API 自己的知識庫 volume。
            document.ToTable("knowledge_documents");
            document.HasKey(d => d.Id);
            document.Property(d => d.Id).ValueGeneratedNever();
            document.Property(d => d.FileName).HasMaxLength(KnowledgeDocument.FileNameMaxLength).IsRequired();
            document.Property(d => d.ContentHash).HasMaxLength(64).IsRequired();
            document.Property(d => d.Status).HasConversion<UpperSnakeCaseEnumConverter<KnowledgeDocumentStatus>>().HasMaxLength(30);
            document.Property(d => d.EmbeddingModel).HasMaxLength(200);
            document.Property(d => d.Error).HasMaxLength(KnowledgeDocument.ErrorMaxLength);
            document.HasIndex(d => new { d.ProjectId, d.Status });
            document.HasIndex(d => d.Status);
            // 同專案同檔名最多一份可查詢的版本（新版本成功後才切換，ADR-0014 §7）。
            document.HasIndex(d => new { d.ProjectId, d.FileName }).IsUnique().HasFilter("[status] = 'READY'");
        });

        modelBuilder.Entity<Site>(site =>
        {
            // ADR-0016：發布的網站；網址代碼由平台產生且唯一。
            site.ToTable("sites");
            site.HasKey(s => s.Id);
            site.Property(s => s.Id).ValueGeneratedNever();
            site.Property(s => s.Slug).HasMaxLength(Site.SlugLength).IsRequired();
            site.Property(s => s.Name).HasMaxLength(Site.NameMaxLength).IsRequired();
            site.Property(s => s.SourcePath).HasMaxLength(Site.SourcePathMaxLength).IsRequired();
            site.Property(s => s.AccessMode).HasConversion<UpperSnakeCaseEnumConverter<SiteAccessMode>>().HasMaxLength(30);
            site.Property(s => s.Status).HasConversion<UpperSnakeCaseEnumConverter<SiteStatus>>().HasMaxLength(30);
            site.HasIndex(s => s.Slug).IsUnique();
            site.HasIndex(s => s.UserId);
            // 一個對話最多一個自動發布的小工具（ADR-0029）；同時完成的執行由唯一索引擋下重複建立。
            site.HasIndex(s => s.ConversationId).IsUnique().HasFilter("[is_tool] = 1");
        });

        modelBuilder.Entity<SiteVersion>(version =>
        {
            version.ToTable("site_versions");
            version.HasKey(v => v.Id);
            version.Property(v => v.Id).ValueGeneratedNever();
            version.Property(v => v.SourcePath).HasMaxLength(Site.SourcePathMaxLength).IsRequired();
            version.Property(v => v.Status).HasConversion<UpperSnakeCaseEnumConverter<SiteVersionStatus>>().HasMaxLength(30);
            version.Property(v => v.Error).HasMaxLength(SiteVersion.ErrorMaxLength);
            version.HasIndex(v => v.SiteId);
            version.HasOne<Site>().WithMany().HasForeignKey(v => v.SiteId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SiteShare>(share =>
        {
            share.ToTable("site_shares");
            share.HasKey(s => new { s.SiteId, s.UserId });
            share.HasIndex(s => s.UserId);
            share.HasOne<Site>().WithMany().HasForeignKey(s => s.SiteId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SiteTicket>(ticket =>
        {
            // ADR-0016 §4：只存雜湊；SiteHost 以條件更新兌換（只能用一次）。
            ticket.ToTable("site_tickets");
            ticket.HasKey(t => t.Id);
            ticket.Property(t => t.Id).ValueGeneratedNever();
            ticket.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
            ticket.HasIndex(t => t.TokenHash).IsUnique();
            ticket.HasIndex(t => t.ExpiresAt);
            ticket.HasOne<Site>().WithMany().HasForeignKey(t => t.SiteId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Message>().Property(m => m.Source).HasMaxLength(20);
        modelBuilder.Entity<ScheduledTask>(task =>
        {
            task.ToTable("scheduled_tasks"); task.HasKey(t => t.Id);
            task.Property(t => t.Id).ValueGeneratedNever();
            task.Property(t => t.Version).IsRowVersion();
            task.Property(t => t.Prompt).HasMaxLength(Message.ContentMaxLength).IsRequired();
            task.Property(t => t.ModelId).HasMaxLength(200);
            task.Property(t => t.ThinkingLevel).HasMaxLength(30);
            task.Property(t => t.CronExpression).HasMaxLength(100);
            task.Property(t => t.TimeZone).HasMaxLength(100);
            task.Property(t => t.CreatedBy).HasMaxLength(20);
            task.Property(t => t.LastResult).HasMaxLength(200);
            task.HasIndex(t => new { t.Status, t.NextRunAt }); task.HasIndex(t => t.UserId);
            task.HasOne<Conversation>().WithMany().HasForeignKey(t => t.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.ApplySnakeCaseNames();
    }
}
