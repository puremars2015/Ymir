using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ymir.Platform.Infrastructure.Persistence;

/// <summary>只供 <c>dotnet ef migrations</c> 使用（不會連線）。</summary>
internal sealed class PlatformDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlServer("Server=localhost;Database=ymir_design;Trusted_Connection=True", SqlServerOptions.Configure)
            .Options);
}
