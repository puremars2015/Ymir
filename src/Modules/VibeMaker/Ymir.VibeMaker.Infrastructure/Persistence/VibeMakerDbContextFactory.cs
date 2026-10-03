using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ymir.VibeMaker.Infrastructure.Persistence;

/// <summary>只供 <c>dotnet ef migrations</c> 使用（不會連線）。</summary>
internal sealed class VibeMakerDbContextFactory : IDesignTimeDbContextFactory<VibeMakerDbContext>
{
    public VibeMakerDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<VibeMakerDbContext>()
            .UseSqlServer("Server=localhost;Database=ymir_design;Trusted_Connection=True", VibeMakerSqlServerOptions.Configure)
            .Options);
}

internal static class VibeMakerSqlServerOptions
{
    public static void Configure(Microsoft.EntityFrameworkCore.Infrastructure.SqlServerDbContextOptionsBuilder sql) =>
        sql.MigrationsHistoryTable("__ef_migrations_history", VibeMakerDbContext.Schema);
}
