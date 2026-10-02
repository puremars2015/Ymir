using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ymir.VibeMaker.Application.Agents;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Infrastructure.Dev;
using Ymir.VibeMaker.Infrastructure.PiAgent;
using Ymir.VibeMaker.Infrastructure.Podman;
using Ymir.VibeMaker.Infrastructure.Runtime;

namespace Ymir.VibeMaker.Infrastructure;

public static class VibeMakerInfrastructureExtensions
{
    /// <summary>
    /// 註冊 Vibe Maker 的 runtime 與 harness。設定：
    /// <c>VibeMaker:Runtime:Provider</c> = Podman | Local；<c>VibeMaker:Harness</c> = Pi | Scripted。
    /// Local runtime 沒有隔離，非 Development 環境會拒絕啟動。
    /// </summary>
    public static IServiceCollection AddVibeMakerInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        services.Configure<RuntimeOptions>(configuration.GetSection(RuntimeOptions.SectionName));
        services.Configure<PiAgentOptions>(configuration.GetSection(PiAgentOptions.SectionName));

        var runtimeProvider = configuration.GetSection(RuntimeOptions.SectionName).GetValue(nameof(RuntimeOptions.Provider), RuntimeProvider.Podman);
        switch (runtimeProvider)
        {
            case RuntimeProvider.Local when !isDevelopment:
                throw new InvalidOperationException("VibeMaker:Runtime:Provider=Local has no isolation and is only allowed in Development.");
            case RuntimeProvider.Local:
                services.AddSingleton<IAgentRuntimeManager, LocalRuntimeManager>();
                break;
            default:
                services.AddSingleton<IAgentRuntimeManager, PodmanRuntimeManager>();
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
}

public enum HarnessKind
{
    Pi = 0,

    /// <summary>固定腳本的假 Agent，不需要 runtime 與 LLM。</summary>
    Scripted = 1,
}
