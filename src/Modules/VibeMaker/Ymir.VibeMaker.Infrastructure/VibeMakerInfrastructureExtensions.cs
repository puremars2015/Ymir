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
using Ymir.VibeMaker.Infrastructure.Runtime.Remote;

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
        services.AddSingleton<Files.RuntimeWorkspaceFileReader>();
        services.AddSingleton<Application.Files.IWorkspaceFileReader>(sp => sp.GetRequiredService<Files.RuntimeWorkspaceFileReader>());
        services.AddSingleton<Application.Files.IWorkspaceFileWriter, Files.RuntimeWorkspaceFileWriter>();
        services.AddSingleton<Application.Extensions.IExtensionInventory, PiAgent.PiExtensionInventory>();
        services.AddHostedService<ExecutionWorker>();
        services.AddHostedService<RuntimeLifecycleWorker>();
        services.AddHttpClient(nameof(Health.LiteLlmHealthCheck));

        // OneDrive connector（ADR-0013）：只有後端連 Graph 與 Entra；token 不保存在 HttpClient。
        services.Configure<Connectors.OneDrive.OneDriveOptions>(configuration.GetSection(Connectors.OneDrive.OneDriveOptions.SectionName));
        services.AddHttpClient<Application.Connectors.OneDrive.IOneDriveOAuthClient, Connectors.OneDrive.OneDriveOAuthClient>(Connectors.OneDrive.OneDriveOAuthClient.HttpClientName);
        services.AddHttpClient<Application.Connectors.OneDrive.IOneDriveClient, Connectors.OneDrive.GraphOneDriveClient>(Connectors.OneDrive.GraphOneDriveClient.HttpClientName);
        services.AddSingleton<Application.Connectors.OneDrive.IOneDriveTokenProtector, Connectors.OneDrive.DataProtectionOneDriveTokenProtector>();
        services.Configure<Application.Connectors.OneDrive.OneDriveSyncOptions>(configuration.GetSection(Connectors.OneDrive.OneDriveOptions.SectionName));
        services.AddHostedService<Connectors.OneDrive.OneDriveSyncWorker>();

        AddKnowledgeBase(services, configuration, isDevelopment);

        // 網站託管（ADR-0016）：沒有設定 Ymir:Sites:BaseUrl / Root 時停用。
        services.Configure<Application.Sites.SiteOptions>(configuration.GetSection(Application.Sites.SiteOptions.SectionName));
        services.AddSingleton<Application.Sites.ISiteStorage, Sites.FileSystemSiteStorage>();

        // 平台 MCP（ADR-0012 B）：設定 gateway 位址時才啟用；目錄與簽章金鑰不合法時拒絕啟動。
        services.Configure<Application.PlatformMcp.McpOptions>(configuration.GetSection(Application.PlatformMcp.McpOptions.SectionName));
        services.AddSingleton(PlatformMcpCatalogLoader.Load(configuration));
        services.AddHealthChecks()
            .AddCheck<Health.DatabaseHealthCheck>("database", tags: [Health.VibeMakerHealthChecks.ReadyTag], timeout: Health.VibeMakerHealthChecks.Timeout)
            .AddCheck<Health.RuntimeHealthCheck>("runtime", tags: [Health.VibeMakerHealthChecks.ReadyTag], timeout: Health.VibeMakerHealthChecks.Timeout)
            .AddCheck<Health.LiteLlmHealthCheck>("litellm", tags: [Health.VibeMakerHealthChecks.ReadyTag], timeout: Health.VibeMakerHealthChecks.Timeout);
        services.Configure<ExecutionOptions>(options =>
        {
            // 部署設定的預設值；管理介面儲存的執行政策優先（ADR-0011）。
            var runtime = configuration.GetSection(RuntimeOptions.SectionName);
            if (runtime.GetValue<double?>(nameof(RuntimeOptions.ExecutionTimeoutMinutes)) is > 0 and var minutes)
            {
                options.Timeout = TimeSpan.FromMinutes(minutes);
            }

            if (runtime.GetValue<double?>(nameof(RuntimeOptions.IdleTimeoutMinutes)) is >= 0 and var idle)
            {
                options.IdleTimeout = TimeSpan.FromMinutes(idle);
            }

            if (runtime.GetValue<int?>(nameof(RuntimeOptions.MaxPendingExecutionsPerUser)) is > 0 and var pending)
            {
                options.MaxPendingExecutionsPerUser = pending;
            }

            if (runtime.GetValue<int?>(nameof(RuntimeOptions.DailyExecutionLimit)) is >= 0 and var daily)
            {
                options.DailyExecutionLimit = daily;
            }

            if (configuration.GetSection(ModelCredentialOptions.SectionName).GetValue<decimal?>(nameof(ExecutionOptions.MonthlyBudgetUsd)) is >= 0 and var budget)
            {
                options.MonthlyBudgetUsd = budget;
            }

            if (runtime.GetValue<double?>(nameof(RuntimeOptions.IdleCheckIntervalSeconds)) is > 0 and var interval)
            {
                options.IdleCheckInterval = TimeSpan.FromSeconds(interval);
            }
        });
        return services.AddVibeMakerAgentRuntime(configuration, isDevelopment);
    }

    /// <summary>
    /// 只註冊 Agent runtime 與 harness（PoC 等不需要資料庫的程式使用）。設定：
    /// <c>VibeMaker:Runtime:Provider</c> = Podman | Docker | Local | Remote；<c>VibeMaker:Harness</c> = Pi | Scripted。
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
        if (runtimeProvider == RuntimeProvider.Remote)
        {
            // API 在容器內：runtime 由主機上的 runtime host 管理（ADR-0008），任何環境都可以使用。
            services.AddSingleton<IAgentRuntimeManager, RemoteRuntimeManager>();
            services.AddSingleton<ITunnelManagement, RuntimeHostTunnelManagement>();
        }
        else
        {
            services.AddVibeMakerRuntimeManager(configuration, isDevelopment);
            services.AddSingleton<ITunnelManagement, UnavailableTunnelManagement>();
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
    /// 只註冊直接管理 runtime 的 provider（Podman / Docker / Local），runtime host（<c>Ymir.RuntimeHost</c>，ADR-0008）使用。
    /// Local runtime 沒有隔離，非 Development 環境會拒絕啟動；Remote 在這裡不允許（runtime host 不能再轉給另一個 runtime host）。
    /// </summary>
    public static IServiceCollection AddVibeMakerRuntimeManager(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        services.Configure<RuntimeOptions>(configuration.GetSection(RuntimeOptions.SectionName));
        var runtimeProvider = configuration.GetSection(RuntimeOptions.SectionName).GetValue(nameof(RuntimeOptions.Provider), RuntimeProvider.Podman);
        switch (runtimeProvider)
        {
            case RuntimeProvider.Remote:
                throw new InvalidOperationException("VibeMaker:Runtime:Provider=Remote cannot be used by the runtime host itself; use Podman.");
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

        return services;
    }

    /// <summary>
    /// 模型金鑰（ADR-0004）：設定了 <c>VibeMaker:LiteLlm:MasterKey</c> 就為每位使用者發 LiteLLM virtual key；
    /// 沒設定時只有 Development 可以退回固定的開發用 key，其他環境拒絕啟動（避免把 master key 當成共用 key 塞進 container）。
    /// </summary>
    /// <summary>
    /// 可選用的模型：<c>VibeMaker:Models</c>（每項 Id、DisplayName、SupportsImages）；預設模型為 <c>VibeMaker:Pi:ModelId</c>，
    /// 不在清單內時自動加入，清單未設定時只有預設模型。
    /// </summary>
    internal static ModelCatalog BuildModelCatalog(IConfiguration configuration)
    {
        var defaultModel = configuration.GetSection(PiAgentOptions.SectionName).GetValue(nameof(PiAgentOptions.ModelId), new PiAgentOptions().ModelId)!;
        var models = configuration.GetSection("VibeMaker:Models").GetChildren()
            .Select(section => (
                Id: section["Id"]?.Trim(),
                DisplayName: section["DisplayName"]?.Trim(),
                SupportsImages: section.GetValue<bool>("SupportsImages"),
                AllowKnowledgeBase: section.GetValue<bool>("AllowKnowledgeBase")))
            .Where(m => !string.IsNullOrEmpty(m.Id))
            .Select(m => new ModelDescriptor(m.Id!, string.IsNullOrEmpty(m.DisplayName) ? m.Id! : m.DisplayName!, m.SupportsImages, m.AllowKnowledgeBase))
            .DistinctBy(m => m.Id)
            .ToList();
        if (!models.Any(m => m.Id == defaultModel))
        {
            models.Insert(0, new ModelDescriptor(defaultModel, defaultModel));
        }

        return new ModelCatalog(models, defaultModel);
    }

    /// <summary>RAG 知識庫（ADR-0014）：沒有設定 <c>VibeMaker:Rag:EmbeddingModel</c> 時停用（worker 不做事、上傳回 409）。</summary>
    private static void AddKnowledgeBase(IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        var rag = configuration.GetSection(Application.Knowledge.KnowledgeOptions.SectionName);
        services.Configure<Application.Knowledge.KnowledgeOptions>(rag);
        var options = rag.Get<Application.Knowledge.KnowledgeOptions>() ?? new Application.Knowledge.KnowledgeOptions();
        var root = configuration.GetSection(Knowledge.KnowledgeStorageOptions.SectionName).Get<Knowledge.KnowledgeStorageOptions>()?.Root;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = options.IsEnabled && !isDevelopment
                ? throw new InvalidOperationException("Ymir:Knowledge:Root is required when VibeMaker:Rag:EmbeddingModel is set (a persistent volume owned by the API, ADR-0014).")
                : Path.Combine(Path.GetTempPath(), "ymir-knowledge");
        }

        var baseUrl = options.BaseUrl
            ?? configuration.GetSection(LiteLlm.LiteLlmOptions.SectionName).Get<LiteLlm.LiteLlmOptions>()?.BaseUrl
            ?? (configuration.GetSection(PiAgentOptions.SectionName).Get<PiAgentOptions>() ?? new PiAgentOptions()).ModelBaseUrl;
        services.AddSingleton(new Knowledge.KnowledgePaths(root));
        services.AddSingleton(new Knowledge.KnowledgeEndpoint(baseUrl));
        services.AddSingleton<Application.Knowledge.IKnowledgeFileStore, Knowledge.FileSystemKnowledgeStore>();
        services.AddSingleton<Application.Knowledge.IVectorStore, Knowledge.SqliteVectorStore>();
        services.AddSingleton<Application.Knowledge.IDocumentTextExtractor, Knowledge.DocumentTextExtractor>();
        services.AddHttpClient<Application.Knowledge.IEmbeddingClient, Knowledge.LiteLlmEmbeddingClient>(Knowledge.LiteLlmEmbeddingClient.HttpClientName);
        services.AddHttpClient<Application.Knowledge.IKnowledgeAnswerClient, Knowledge.LiteLlmAnswerClient>(Knowledge.LiteLlmAnswerClient.HttpClientName);
        services.AddHostedService<Knowledge.KnowledgeIndexWorker>();
    }

    private static void AddModelGateway(IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        var section = configuration.GetSection(LiteLlmOptions.SectionName);
        services.Configure<LiteLlmOptions>(section);
        services.Configure<ModelCredentialOptions>(section);
        var catalog = BuildModelCatalog(configuration);
        services.AddSingleton(catalog);
        // 沒有設定 AllowedModels 時只允許模型清單內的模型，不發出不限模型的 key。
        services.PostConfigure<ModelCredentialOptions>(options =>
        {
            if (options.AllowedModels.Count == 0)
            {
                foreach (var model in catalog.Models)
                {
                    options.AllowedModels.Add(model.Id);
                }
            }

            // 知識庫的 embedding 模型（ADR-0014 §3）也以使用者的 virtual key 呼叫。
            if (configuration.GetValue<string>($"{Application.Knowledge.KnowledgeOptions.SectionName}:EmbeddingModel") is { Length: > 0 } embedding
                && !options.AllowedModels.Contains(embedding))
            {
                options.AllowedModels.Add(embedding);
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

/// <summary>啟動時載入平台 MCP 服務目錄（ADR-0012 B.1）。</summary>
internal static class PlatformMcpCatalogLoader
{
    public static Application.PlatformMcp.McpCatalog Load(IConfiguration configuration)
    {
        var options = configuration.GetSection(Application.PlatformMcp.McpOptions.SectionName).Get<Application.PlatformMcp.McpOptions>()
            ?? new Application.PlatformMcp.McpOptions();
        if (!options.IsEnabled)
        {
            return Application.PlatformMcp.McpCatalog.Empty;
        }

        Application.PlatformMcp.McpGatewayToken.EnsureKeyIsStrong(options.TokenSigningKey, "Ymir:Mcp:TokenSigningKey");
        return string.IsNullOrWhiteSpace(options.CatalogPath)
            ? throw new InvalidOperationException("Ymir:Mcp:CatalogPath is required when Ymir:Mcp:GatewayUrl is set.")
            : Application.PlatformMcp.McpCatalog.Load(options.CatalogPath);
    }
}

