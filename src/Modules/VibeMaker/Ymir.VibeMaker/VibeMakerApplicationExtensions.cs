using Microsoft.Extensions.DependencyInjection;
using Ymir.VibeMaker.Application.Conversations;
using Ymir.VibeMaker.Application.Workspaces;

namespace Ymir.VibeMaker;

public static class VibeMakerApplicationExtensions
{
    public static IServiceCollection AddVibeMakerApplication(this IServiceCollection services)
    {
        services.AddScoped<WorkspaceService>();
        services.AddScoped<ConversationService>();
        return services;
    }
}
