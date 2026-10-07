using Microsoft.Extensions.DependencyInjection;
using Ymir.VibeMaker.Application.Conversations;
using Ymir.VibeMaker.Application.Executions;
using Ymir.VibeMaker.Application.Models;
using Ymir.VibeMaker.Application.Projects;
using Ymir.VibeMaker.Application.Settings;

namespace Ymir.VibeMaker;

public static class VibeMakerApplicationExtensions
{
    public static IServiceCollection AddVibeMakerApplication(this IServiceCollection services)
    {
        services.AddScoped<ProjectService>();
        services.AddScoped<RuntimeQueryService>();
        services.AddScoped<UserSettingsService>();
        services.AddScoped<ConversationService>();
        services.AddScoped<ExecutionService>();
        services.AddScoped<Application.Make.MakeTopicService>();
        services.AddScoped<Application.Users.UserDeactivationService>();
        services.AddScoped<Application.Admin.AdminStatsService>();
        services.AddScoped<Application.Files.WorkspaceFileService>();
        services.AddScoped<Application.Files.ArtifactService>();
        services.AddScoped<Application.Attachments.AttachmentService>();
        services.AddScoped<ExecutionEventWriter>();
        services.AddScoped<ExecutionRunner>();
        services.AddScoped<ExecutionReconciler>();
        services.AddScoped<ExecutionEventRetention>();
        services.AddScoped<Application.Runtime.RuntimeLifecycleService>();
        services.AddSingleton<Application.Runtime.RuntimePolicyService>();
        services.AddSingleton<Application.Extensions.ExtensionPolicyService>();
        services.AddSingleton<Application.Extensions.IExtensionPolicy>(sp => sp.GetRequiredService<Application.Extensions.ExtensionPolicyService>());
        services.AddSingleton<UserExecutionLocks>();
        services.AddSingleton<VibeMakerTelemetry>();
        services.AddSingleton<RuntimeCredentialService>();
        services.AddSingleton<ModelBudgetGuard>();
        return services;
    }
}
