using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Users;

namespace Ymir.Platform.Infrastructure.Persistence;

/// <summary>共用核心的資料（schema <c>platform</c>，ADR-0001）。</summary>
public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public const string Schema = "platform";

    public DbSet<User> Users => Set<User>();

    public DbSet<LocalCredential> LocalCredentials => Set<LocalCredential>();

    internal DbSet<AuditLogRecord> AuditLog => Set<AuditLogRecord>();

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

        modelBuilder.ApplySnakeCaseNames();
    }
}
