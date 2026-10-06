using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ymir.Platform.Auditing;
using Ymir.Platform.Identity;
using Ymir.Platform.Infrastructure.Auditing;
using Ymir.Platform.Infrastructure.Identity;
using Ymir.Platform.Infrastructure.Persistence;
using Ymir.Platform.Infrastructure.Settings;
using Ymir.Platform.Infrastructure.Users;
using Ymir.Platform.Settings;
using Ymir.Platform.Users;

namespace Ymir.Platform.Infrastructure;

public static class PlatformInfrastructureExtensions
{
    public static IServiceCollection AddPlatformInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<PlatformDbContext>(options => options.UseSqlServer(connectionString, SqlServerOptions.Configure));
        services.AddSingleton<IAuditLog, DbAuditLog>();
        services.AddScoped<IAuditLogQuery, DbAuditLogQuery>();
        services.AddScoped<ISystemSettingsStore, SystemSettingsStore>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<ILocalAccountService, LocalAccountService>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
