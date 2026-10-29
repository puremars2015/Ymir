using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Messaging;
using Ymir.Platform.Notifications;
using Ymir.Platform.Settings;
using Ymir.Platform.Users;

namespace Ymir.Platform.Infrastructure.Persistence;

/// <summary>共用核心的資料（schema <c>platform</c>，ADR-0001）。</summary>
public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public const string Schema = "platform";

    public DbSet<User> Users => Set<User>();

    public DbSet<LocalCredential> LocalCredentials => Set<LocalCredential>();

    internal DbSet<AuditLogRecord> AuditLog => Set<AuditLogRecord>();

    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    public DbSet<DirectMessage> DirectMessages => Set<DirectMessage>();

    public DbSet<ChatGroup> ChatGroups => Set<ChatGroup>();

    public DbSet<ChatGroupMember> ChatGroupMembers => Set<ChatGroupMember>();

    public DbSet<ChatGroupMessage> ChatGroupMessages => Set<ChatGroupMessage>();

    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<User>(user =>
        {
            user.ToTable("users");
            user.HasKey(u => u.Id);
            user.Property(u => u.Id).ValueGeneratedNever();
            user.Property(u => u.Issuer).HasMaxLength(300).IsRequired();
            user.Property(u => u.Subject).HasMaxLength(200).IsRequired();
            user.Property(u => u.AccountName).HasMaxLength(200);
            user.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
            user.Property(u => u.Email).HasMaxLength(320);
            user.Property(u => u.Department).HasMaxLength(200);
            user.Property(u => u.Role).HasConversion<UpperSnakeCaseEnumConverter<UserRole>>().HasMaxLength(30);
            user.Property(u => u.Status).HasConversion<UpperSnakeCaseEnumConverter<UserStatus>>().HasMaxLength(30);
            user.HasIndex(u => new { u.Issuer, u.Subject }).IsUnique();
        });

        // 本機帳號的密碼雜湊（ADR-0009）；同一模組內，可以建 FK。
        modelBuilder.Entity<LocalCredential>(credential =>
        {
            credential.ToTable("local_credentials");
            credential.HasKey(c => c.UserId);
            credential.Property(c => c.UserId).ValueGeneratedNever();
            credential.Property(c => c.PasswordHash).HasMaxLength(500).IsRequired();
            credential.HasOne<User>().WithOne().HasForeignKey<LocalCredential>(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditLogRecord>(audit =>
        {
            audit.ToTable("audit_log");
            audit.HasKey(a => a.Id);
            audit.Property(a => a.Actor).HasMaxLength(200).IsRequired();
            audit.Property(a => a.Action).HasMaxLength(100).IsRequired();
            audit.Property(a => a.TargetType).HasMaxLength(100).IsRequired();
            audit.Property(a => a.TargetId).HasMaxLength(200).IsRequired();
            audit.Property(a => a.Result).HasMaxLength(30).IsRequired();
            audit.Property(a => a.CorrelationId).HasMaxLength(100);
            audit.HasIndex(a => a.Timestamp);
            audit.HasIndex(a => new { a.TargetType, a.TargetId });
            // 稽核頁依動作前綴與操作者篩選（ADR-0010）
            audit.HasIndex(a => a.Action);
            audit.HasIndex(a => a.Actor);
        });

        modelBuilder.Entity<SystemSetting>(setting =>
        {
            setting.ToTable("system_settings");
            setting.HasKey(s => s.Key);
            setting.Property(s => s.Key).HasMaxLength(SystemSetting.KeyMaxLength);
            setting.Property(s => s.Value).HasMaxLength(SystemSetting.ValueMaxLength).IsRequired();
            setting.Property(s => s.UpdatedBy).HasMaxLength(200).IsRequired();
        });

        // 使用者之間的訊息（ADR-0023）；同一模組內，可以建 FK。兩個 FK 都指向 users，SQL Server 不允許兩條串聯刪除路徑，所以不串聯
        //（使用者刪除只是標示 Deleted，資料列保留，ADR-0027）。
        modelBuilder.Entity<DirectMessage>(message =>
        {
            message.ToTable("direct_messages");
            message.HasKey(m => m.Id);
            message.Property(m => m.Id).ValueGeneratedNever();
            message.Property(m => m.Body).HasMaxLength(DirectMessage.BodyMaxLength).IsRequired();
            message.HasOne<User>().WithMany().HasForeignKey(m => m.SenderId).OnDelete(DeleteBehavior.NoAction);
            message.HasOne<User>().WithMany().HasForeignKey(m => m.RecipientId).OnDelete(DeleteBehavior.NoAction);
            // 兩人之間的歷史（兩個方向各用一個索引）與未讀數。
            message.HasIndex(m => new { m.SenderId, m.RecipientId, m.CreatedAt });
            message.HasIndex(m => new { m.RecipientId, m.SenderId, m.CreatedAt });
            message.HasIndex(m => new { m.RecipientId, m.ReadAt });
        });

        // 站內通知（ADR-0032 §1）：只屬於一位使用者；使用者刪除只是標示 Deleted（ADR-0027），所以不串聯刪除。
        modelBuilder.Entity<Notification>(notification =>
        {
            notification.ToTable("notifications");
            notification.HasKey(n => n.Id);
            notification.Property(n => n.Id).ValueGeneratedNever();
            notification.HasIndex(n => new { n.UserId, n.Kind, n.SourceId }).IsUnique()
                .HasFilter("[source_id] IS NOT NULL AND [kind] = 'REMINDER'");
            notification.Property(n => n.Kind).HasConversion<UpperSnakeCaseEnumConverter<NotificationKind>>().HasMaxLength(40);
            notification.Property(n => n.Title).HasMaxLength(Notification.TitleMaxLength).IsRequired();
            notification.Property(n => n.Body).HasMaxLength(Notification.BodyMaxLength);
            notification.Property(n => n.Link).HasMaxLength(Notification.LinkMaxLength);
            notification.HasOne<User>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.NoAction);
            // 清單（依 id 由新到舊）、未讀數與保存期限。
            notification.HasIndex(n => new { n.UserId, n.CreatedAt });
            notification.HasIndex(n => new { n.UserId, n.ReadAt });
            notification.HasIndex(n => n.CreatedAt);
        });

        // 群組聊天（ADR-0026）；同一模組內可以建 FK。使用者刪除只是標示 Deleted（ADR-0027），所以不串聯刪除使用者。
        modelBuilder.Entity<ChatGroup>(group =>
        {
            group.ToTable("chat_groups");
            group.HasKey(g => g.Id);
            group.Property(g => g.Id).ValueGeneratedNever();
            group.Property(g => g.Name).HasMaxLength(ChatGroup.NameMaxLength).IsRequired();
            group.Property(g => g.Status).HasConversion<UpperSnakeCaseEnumConverter<ChatGroupStatus>>().HasMaxLength(30);
            group.HasOne<User>().WithMany().HasForeignKey(g => g.OwnerId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ChatGroupMember>(member =>
        {
            member.ToTable("chat_group_members");
            member.HasKey(m => new { m.GroupId, m.UserId });
            member.Property(m => m.Role).HasConversion<UpperSnakeCaseEnumConverter<ChatGroupRole>>().HasMaxLength(30);
            member.HasOne<ChatGroup>().WithMany().HasForeignKey(m => m.GroupId).OnDelete(DeleteBehavior.Cascade);
            member.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.NoAction);
            // 「我所屬的群組」。
            member.HasIndex(m => m.UserId);
        });

        modelBuilder.Entity<ChatGroupMessage>(message =>
        {
            message.ToTable("chat_group_messages");
            message.HasKey(m => m.Id);
            message.Property(m => m.Id).ValueGeneratedNever();
            message.Property(m => m.SenderKind).HasConversion<UpperSnakeCaseEnumConverter<GroupSenderKind>>().HasMaxLength(30);
            message.Property(m => m.BotStatus).HasConversion<UpperSnakeCaseEnumConverter<BotReplyStatus>>().HasMaxLength(30);
            message.Property(m => m.Body).HasMaxLength(ChatGroupMessage.BotBodyMaxLength + 1).IsRequired();
            message.HasOne<ChatGroup>().WithMany().HasForeignKey(m => m.GroupId).OnDelete(DeleteBehavior.Cascade);
            message.HasOne<User>().WithMany().HasForeignKey(m => m.SenderId).OnDelete(DeleteBehavior.NoAction);
            message.HasIndex(m => new { m.GroupId, m.CreatedAt });
            // 背景工作找 Pending 的 Bot 回覆；一個 execution 只對應一則 Bot 訊息。
            message.HasIndex(m => m.BotStatus).HasFilter("[bot_status] = 'PENDING'");
            message.HasIndex(m => m.ExecutionId).IsUnique().HasFilter("[execution_id] IS NOT NULL");
        });

        modelBuilder.ApplySnakeCaseNames();
    }
}
