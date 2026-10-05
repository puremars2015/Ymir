using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ymir.VibeMaker.Application.Agents;
using Ymir.VibeMaker.Application.Executions;
using Ymir.VibeMaker.Application.Models;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Infrastructure.Containers;
using Ymir.VibeMaker.Infrastructure.Dev;
using Ymir.VibeMaker.Infrastructure.Executions;
using Ymir.VibeMaker.Infrastructure.LiteLlm;
using Ymir.VibeMaker.Infrastructure.Persistence;
using Ymir.VibeMaker.Infrastructure.PiAgent;
using Ymir.VibeMaker.Infrastructure.Runtime;

namespace Ymir.VibeMaker.Infrastructure;

public static class VibeMakerInfrastructureExtensions
{
    /// <summary>註冊 Vibe Maker 模組全部的 Infrastructure：資料庫 + Agent runtime / harness。</summary>
    public static IServiceCollection AddVibeMakerInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString,
        bool isDevelopment)
    {
        services.AddDbContext<VibeMakerDbContext>(options => options.UseSqlServer(connectionString, VibeMakerSqlServerOptions.Configure));
        services.AddScoped<IVibeMakerDbContext>(sp => sp.GetRequiredService<VibeMakerDbContext>());

        services.AddSingleton<IExecutionDispatcher, ChannelExecutionDispatcher>();
        services.AddSingleton<IExecutionEventBus, InMemoryExecutionEventBus>();
        services.AddSingleton<IExecutionCancellationRegistry, ExecutionCancellationRegistry>();
        services.AddHostedService<ExecutionWorker>();
        services.Configure<ExecutionOptions>(options =>
        {
            var minutes = configuration.GetSection(RuntimeOptions.SectionName).GetValue<double?>(nameof(RuntimeOptions.ExecutionTimeoutMinutes));
            if (minutes is > 0)
            {
                options.Timeout = TimeSpan.FromMinutes(minutes.Value);
            }
        });
        return services.AddVibeMakerAgentRuntime(configuration, isDevelopment);
    }

    /// <summary>
    /// 只註冊 Agent runtime 與 harness（PoC 等不需要資料庫的程式使用）。設定：
    /// <c>VibeMaker:Runtime:Provider</c> = Podman | Docker | Local；<c>VibeMaker:Harness</c> = Pi | Scripted。
    /// Local runtime 沒有隔離，非 Development 環境會拒絕啟動。
    /// </summary>
    public static IServiceCollection AddVibeMakerAgentRuntime(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        services.Configure<RuntimeOptions>(configuration.GetSection(RuntimeOptions.SectionName));
        services.Configure<PiAgentOptions>(configuration.GetSection(PiAgentOptions.SectionName));
        AddModelGateway(services, configuration, isDevelopment);

        var runtimeProvider = configuration.GetSection(RuntimeOptions.SectionName).GetValue(nameof(RuntimeOptions.Provider), RuntimeProvider.Podman);
        switch (runtimeProvider)
        {
            case RuntimeProvider.Local when !isDevelopment:
                throw new InvalidOperationException("VibeMaker:Runtime:Provider=Local has no isolation and is only allowed in Development.");
            case RuntimeProvider.Local when OperatingSystem.IsWindows():
                // Local runtime 需要 sh 與 Linux 路徑；Windows 請改用 Provider=Docker（docs/guides/windows-docker.md）。
                throw new InvalidOperationException("VibeMaker:Runtime:Provider=Local is not supported on Windows; use Provider=Docker.");
            case RuntimeProvider.Local:
                services.AddSingleton<IAgentRuntimeManager, LocalRuntimeManager>();
                break;
            default:
                services.AddSingleton<IAgentRuntimeManager, ContainerRuntimeManager>();
                break;
        }

        var harness = configuration.GetValue("VibeMaker:Harness", HarnessKind.Pi);
        if (harness == HarnessKind.Scripted)
        {
            services.AddSingleton<IAgentHarness, ScriptedAgentHarness>();
        }
        else
        {
            services.AddSingleton<IAgentHarness, PiAgentHarness>();
        }

        return services;
    }

    /// <summary>
    /// 模型金鑰（ADR-0004）：設定了 <c>VibeMaker:LiteLlm:MasterKey</c> 就為每位使用者發 LiteLLM virtual key；
    /// 沒設定時只有 Development 可以退回固定的開發用 key，其他環境拒絕啟動（避免把 master key 當成共用 key 塞進 container）。
    /// </summary>
    private static void AddModelGateway(IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        var section = configuration.GetSection(LiteLlmOptions.SectionName);
        services.Configure<LiteLlmOptions>(section);
        services.Configure<ModelCredentialOptions>(section);
        // 沒有設定 AllowedModels 時只允許 Pi 使用的模型，不發出不限模型的 key。
        var piModel = configuration.GetSection(PiAgentOptions.SectionName).GetValue(nameof(PiAgentOptions.ModelId), new PiAgentOptions().ModelId)!;
        services.PostConfigure<ModelCredentialOptions>(options =>
        {
            if (options.AllowedModels.Count == 0)
            {
                options.AllowedModels.Add(piModel);
            }
        });
        services.TryAddSingleton(TimeProvider.System);

        var liteLlm = section.Get<LiteLlmOptions>() ?? new LiteLlmOptions();
        if (liteLlm.IsConfigured)
        {
            if (liteLlm.BaseUrl is null)
            {
                throw new InvalidOperationException("VibeMaker:LiteLlm:BaseUrl is required when MasterKey is configured.");
            }

            services.AddHttpClient<IModelGateway, LiteLlmModelGateway>(LiteLlmModelGateway.HttpClientName);
            return;
        }

        var harness = configuration.GetValue("VibeMaker:Harness", HarnessKind.Pi);
        if (!isDevelopment && harness == HarnessKind.Pi)
        {
            throw new InvalidOperationException("VibeMaker:LiteLlm:MasterKey and BaseUrl are required outside Development (per-user virtual keys, ADR-0004).");
        }

        services.AddSingleton<IModelGateway, DevelopmentModelGateway>();
    }
}

public enum HarnessKind
{
    Pi = 0,

    /// <summary>固定腳本的假 Agent，不需要 runtime 與 LLM。</summary>
    Scripted = 1,
}
