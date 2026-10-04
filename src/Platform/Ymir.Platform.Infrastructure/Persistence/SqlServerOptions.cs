using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Ymir.Platform.Infrastructure.Persistence;

internal static class SqlServerOptions
{
    /// <summary>每個 schema 有自己的 migration 歷史表，模組可以各自演進。</summary>
    public static void Configure(SqlServerDbContextOptionsBuilder sql) =>
        sql.MigrationsHistoryTable("__ef_migrations_history", PlatformDbContext.Schema)
            .EnableRetryOnFailure(3);
}
