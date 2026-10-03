using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Infrastructure.Persistence;
using Ymir.VibeMaker.Infrastructure.Persistence;

namespace Ymir.Api.Infrastructure;

/// <summary>
/// 套用各模組的 EF Core migrations。只在 Development / 測試自動執行；
/// 正式環境以部署流程（產生 SQL script 審核後執行）處理，避免應用程式持有 DDL 權限。
/// </summary>
internal static class DatabaseMigrator
{
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().Database.MigrateAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<VibeMakerDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
