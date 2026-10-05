using Microsoft.Extensions.DependencyInjection;
using Ymir.VibeMaker.Application.Conversations;
using Ymir.VibeMaker.Application.Executions;
using Ymir.VibeMaker.Application.Models;
using Ymir.VibeMaker.Application.Projects;

namespace Ymir.VibeMaker;

public static class VibeMakerApplicationExtensions
{
    public static IServiceCollection AddVibeMakerApplication(this IServiceCollection services)
    {
        services.AddScoped<ProjectService>();
        services.AddScoped<RuntimeQueryService>();
        services.AddScoped<ConversationService>();
        services.AddScoped<ExecutionService>();
        services.AddScoped<ExecutionEventWriter>();
        services.AddScoped<ExecutionRunner>();
        services.AddScoped<ExecutionReconciler>();
        services.AddSingleton<UserExecutionLocks>();
        services.AddSingleton<RuntimeCredentialService>();
        return services;
    }
}
