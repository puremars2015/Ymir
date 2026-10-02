using Microsoft.Extensions.DependencyInjection;
using Ymir.Platform.Auditing;
using Ymir.Platform.Infrastructure.Auditing;

namespace Ymir.Platform.Infrastructure;

public static class PlatformInfrastructureExtensions
{
    public static IServiceCollection AddPlatformInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IAuditLog, LoggerAuditLog>();
        return services;
    }
}
