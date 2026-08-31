using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Connections;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.ResponseHandling;
using CrestApps.Core.AI.Security;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Speech;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.AI.Tools;
using CrestApps.Core.Builders;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Services;
using CrestApps.Core.Templates;
using CrestApps.Core.Templates.Extensions;
using CrestApps.Core.Templates.Parsing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI;

/// <summary>
/// Provides extension methods for service Collection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the reusable templating services plus the built-in AI template source definitions.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The action used to configure.</param>
    public static IServiceCollection AddCoreAITemplating(
        this IServiceCollection services,
        Action<TemplateOptions> configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddTemplating(configure)
            .AddCoreAITemplateSource(AITemplateSources.Profile, entry =>
            {
                entry.DisplayName = new LocalizedString(AITemplateSources.Profile, "Profile");
                entry.Description = new LocalizedString(AITemplateSources.Profile, "Create a template that can be applied to AI profiles.");
            })
            .AddCoreAITemplateSource(AITemplateSources.SystemPrompt, entry =>
            {
                entry.DisplayName = new LocalizedString(AITemplateSources.SystemPrompt, "System Prompt");
                entry.Description = new LocalizedString(AITemplateSources.SystemPrompt, "Create a reusable system prompt template.");
            });

        services.Configure<Fluid.TemplateOptions>(options =>
        {
            Fluid.MemberAccessStrategyExtensions.Register<AIToolDefinitionEntry>(options.MemberAccessStrategy);
            Fluid.MemberAccessStrategyExtensions.Register<ChatDocumentInfo>(options.MemberAccessStrategy);
            Fluid.MemberAccessStrategyExtensions.Register<ToolRegistryEntry>(options.MemberAccessStrategy);
        });

        services.TryAddScoped<IAIProfileTemplateManager, DefaultAIProfileTemplateManager>();
        services.TryAddScoped<ISourceCatalogManager<AIProfileTemplate>>(sp => (ISourceCatalogManager<AIProfileTemplate>)sp.GetRequiredService<IAIProfileTemplateManager>());
        services.TryAddScoped<INamedSourceCatalogManager<AIProfileTemplate>>(sp => sp.GetRequiredService<IAIProfileTemplateManager>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICatalogEntryHandler<AIProfileTemplate>, AIProfileTemplateCatalogHandler>());

        return services;
    }

    /// <summary>
    /// Registers an AI tool with the builder pattern for fluent configuration.
    /// By default, tools are registered as system tools (hidden from UI).
    /// Call <see cref="AIToolBuilder{TTool}.Selectable"/> to make the tool visible for user selection.
    /// </summary>
    /// <typeparam name="TTool">The tool type implementing <see cref="AITool"/>.</typeparam>
    /// <returns>A builder for fluent configuration of the tool.</returns>
    public static AIToolBuilder<TTool> AddCoreAITool<TTool>(this IServiceCollection services, string name)
        where TTool : AITool
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);

        services.AddCoreAIToolServices<TTool>(name);

        var entry = new AIToolDefinitionEntry(typeof(TTool))
        {
            Name = name,
            IsSystemTool = true,
        };

        services.Configure<AIToolDefinitionOptions>(o =>
        {
            if (string.IsNullOrEmpty(entry.Title))
            {
                entry.Title = name;
            }

            if (string.IsNullOrEmpty(entry.Description))
            {
                entry.Description = name;
            }

            o.SetTool(name, entry);
        });

        return new AIToolBuilder<TTool>(entry);
    }

    /// <summary>
    /// Registers the core DI services for an AI tool (singleton and keyed singleton)
    /// without adding it to the tool definition options. Use this for tools that
    /// should only be resolved programmatically (e.g., MCP invoke function).
    /// </summary>
    public static IServiceCollection AddCoreAIToolServices<TTool>(this IServiceCollection services, string name)
        where TTool : AITool
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);

        services.AddSingleton<TTool>();
        services.AddKeyedSingleton<AITool>(name, (sp, key) => sp.GetRequiredService<TTool>());

        return services;
    }

    /// <summary>
    /// Adds core CrestApps AI services to the service collection.
    /// This is the main entry point for any ASP.NET Core application to use CrestApps AI.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Ensure IHttpContextAccessor is available for services that need HTTP context.

        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.TryAddSingleton(TimeProvider.System);
        services
            .AddCoreAITemplating()
            .AddCoreIndexingServices()
            .AddCoreServices()
            .AddOptions<AIProviderConnectionCatalogOptions>().Services
            .AddOptions<AIDeploymentCatalogOptions>().Services
            .AddScoped<IAIClientFactory, DefaultAIClientFactory>()
            .AddScoped<ISpeechVoiceResolver, DefaultSpeechVoiceResolver>()
            .AddScoped<IRealtimeVoiceResolver, DefaultRealtimeVoiceResolver>();

        // Register the multi-source stores used for merged runtime lookups.
        services.TryAddScoped<IAIDeploymentStore, DefaultAIDeploymentStore>();

        services.TryAddScoped<IAIProviderConnectionStore, DefaultAIProviderConnectionStore>();

        // Register the configuration-backed sources (Order=100, lower priority than DB).
        services.TryAddEnumerable(ServiceDescriptor.Scoped<INamedSourceCatalogSource<AIDeployment>, ConfigurationAIDeploymentSource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<INamedSourceCatalogSource<AIProviderConnection>, ConfigurationAIProviderConnectionSource>());

        services.TryAddSingleton<IAITextNormalizer, DefaultAITextNormalizer>();
        services.TryAddScoped<IOAuth2TokenService, DefaultOAuth2TokenService>();
        services.TryAddScoped<IConnectionAuthHeaderBuilder, DefaultConnectionAuthHeaderBuilder>();
        services.TryAddScoped<IAIProfileManager, DefaultAIProfileManager>();
        services.TryAddScoped<INamedCatalogManager<AIProfile>>(sp => sp.GetRequiredService<IAIProfileManager>());

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(EmbeddedResourceAIProfileTemplateProvider)))
        {
            services.AddSingleton(sp =>
                new EmbeddedResourceAIProfileTemplateProvider(
                    typeof(ServiceCollectionExtensions).Assembly,
                    sp.GetServices<ITemplateParser>()));
            services.AddSingleton<IAIProfileTemplateProvider>(sp =>
                sp.GetRequiredService<EmbeddedResourceAIProfileTemplateProvider>());
        }

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAIProfileTemplateProvider, AIProfileFileSystemTemplateProvider>());

        services.TryAddScoped<IAICompletionService, DefaultAICompletionService>();
        services.TryAddScoped<IAICompletionContextBuilder, DefaultAICompletionContextBuilder>();
        services.TryAddScoped<IAICompletionUsageService, DefaultAICompletionUsageService>();
        services.TryAddScoped<IAICompletionUsageObserver>(sp => sp.GetRequiredService<IAICompletionUsageService>());

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAICompletionContextBuilderHandler, AIProfileCompletionContextBuilderHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICatalogEntryHandler<AIProfile>, AIProfileHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICatalogEntryHandler<AIDeployment>, AIDeploymentCatalogHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICatalogEntryHandler<AIProviderConnection>, AIProviderConnectionCatalogHandler>());

        services.AddCoreAIModelCapabilities();

        return services;
    }

    /// <summary>
    /// Adds the metadata-driven model capability services along with the model features and
    /// model parameters that ship with the framework.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIModelCapabilities(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<AIModelCapabilityOptions>();
        services.TryAddScoped<IAIModelCapabilityService, DefaultAIModelCapabilityService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAICompletionServiceHandler, ModelParametersAICompletionServiceHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIModelParameterBinder, ReasoningEffortModelParameterBinder>());

        services
            .AddAIModelFeature(AIModelFeatureNames.ToolCalling, new LocalizedString(AIModelFeatureNames.ToolCalling, "Tool calling"), feature =>
            {
                feature.Description = new LocalizedString(AIModelFeatureNames.ToolCalling, "The model can call tools and functions supplied with the request.");
                feature.Order = 10;
                feature.EnabledByDefault = true;
            })
            .AddAIModelFeature(AIModelFeatureNames.StructuredOutputs, new LocalizedString(AIModelFeatureNames.StructuredOutputs, "Structured outputs"), feature =>
            {
                feature.Description = new LocalizedString(AIModelFeatureNames.StructuredOutputs, "The model can return responses that follow a supplied JSON schema.");
                feature.Order = 20;
            })
            .AddAIModelFeature(AIModelFeatureNames.Streaming, new LocalizedString(AIModelFeatureNames.Streaming, "Streaming"), feature =>
            {
                feature.Description = new LocalizedString(AIModelFeatureNames.Streaming, "The model can stream response updates as they are produced.");
                feature.Order = 30;
                feature.EnabledByDefault = true;
            })
            .AddAIModelFeature(AIModelFeatureNames.Reasoning, new LocalizedString(AIModelFeatureNames.Reasoning, "Reasoning"), feature =>
            {
                feature.Description = new LocalizedString(AIModelFeatureNames.Reasoning, "The model performs internal reasoning before producing an answer.");
                feature.Order = 40;
            })
            .AddAIModelFeature(AIModelFeatureNames.ImageInput, new LocalizedString(AIModelFeatureNames.ImageInput, "Image input (vision)"), feature =>
            {
                feature.Description = new LocalizedString(AIModelFeatureNames.ImageInput, "The model can understand image inputs.");
                feature.Order = 50;
            })
            .AddAIModelFeature(AIModelFeatureNames.ImageOutput, new LocalizedString(AIModelFeatureNames.ImageOutput, "Image output"), feature =>
            {
                feature.Description = new LocalizedString(AIModelFeatureNames.ImageOutput, "The model can generate images.");
                feature.Order = 60;
            })
            .AddAIModelFeature(AIModelFeatureNames.AudioInput, new LocalizedString(AIModelFeatureNames.AudioInput, "Audio input"), feature =>
            {
                feature.Description = new LocalizedString(AIModelFeatureNames.AudioInput, "The model accepts audio input.");
                feature.Order = 70;
            })
            .AddAIModelFeature(AIModelFeatureNames.AudioOutput, new LocalizedString(AIModelFeatureNames.AudioOutput, "Audio output"), feature =>
            {
                feature.Description = new LocalizedString(AIModelFeatureNames.AudioOutput, "The model produces audio output.");
                feature.Order = 80;
            })
            .AddAIModelFeature(AIModelFeatureNames.VideoInput, new LocalizedString(AIModelFeatureNames.VideoInput, "Video input"), feature =>
            {
                feature.Description = new LocalizedString(AIModelFeatureNames.VideoInput, "The model can understand video inputs.");
                feature.Order = 90;
            })
            .AddAIModelFeature(AIModelFeatureNames.VideoOutput, new LocalizedString(AIModelFeatureNames.VideoOutput, "Video output"), feature =>
            {
                feature.Description = new LocalizedString(AIModelFeatureNames.VideoOutput, "The model can generate video.");
                feature.Order = 100;
            });

        services.AddAIModelParameter(AIModelParameterNames.ReasoningEffort, new LocalizedString(AIModelParameterNames.ReasoningEffort, "Reasoning effort"), parameter =>
        {
            parameter.Description = new LocalizedString(AIModelParameterNames.ReasoningEffort, "Controls how much internal reasoning the model applies before answering. Higher values produce more thoughtful answers with increased latency and cost.");
            parameter.Kind = AIModelParameterKind.Choice;
            parameter.DefaultValue = nameof(ReasoningEffort.Medium);
            parameter.RequiredFeature = AIModelFeatureNames.Reasoning;
            parameter.Order = 10;
            parameter.AllowedValues =
            [
                new AIModelParameterOption
                {
                    Value = nameof(ReasoningEffort.None),
                    DisplayName = new LocalizedString(nameof(ReasoningEffort.None), "Minimal"),
                },
                new AIModelParameterOption
                {
                    Value = nameof(ReasoningEffort.Low),
                    DisplayName = new LocalizedString(nameof(ReasoningEffort.Low), "Low"),
                },
                new AIModelParameterOption
                {
                    Value = nameof(ReasoningEffort.Medium),
                    DisplayName = new LocalizedString(nameof(ReasoningEffort.Medium), "Medium"),
                },
                new AIModelParameterOption
                {
                    Value = nameof(ReasoningEffort.High),
                    DisplayName = new LocalizedString(nameof(ReasoningEffort.High), "High"),
                },
                new AIModelParameterOption
                {
                    Value = nameof(ReasoningEffort.ExtraHigh),
                    DisplayName = new LocalizedString(nameof(ReasoningEffort.ExtraHigh), "Extra high"),
                },
            ];
        });

        return services;
    }

    /// <summary>
    /// Registers a model feature that deployments can declare support for.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The technical name of the feature.</param>
    /// <param name="displayName">The display text shown to operators.</param>
    /// <param name="configure">An optional delegate used to further configure the descriptor.</param>
    public static IServiceCollection AddAIModelFeature(
        this IServiceCollection services,
        string name,
        LocalizedString displayName,
        Action<AIModelFeatureDescriptor> configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return services.Configure<AIModelCapabilityOptions>(options => options.AddFeature(name, displayName, configure));
    }

    /// <summary>
    /// Registers a model parameter that deployments can declare support for.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The technical name of the parameter.</param>
    /// <param name="displayName">The display text shown to operators.</param>
    /// <param name="configure">An optional delegate used to further configure the descriptor.</param>
    public static IServiceCollection AddAIModelParameter(
        this IServiceCollection services,
        string name,
        LocalizedString displayName,
        Action<AIModelParameterDescriptor> configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return services.Configure<AIModelCapabilityOptions>(options => options.AddParameter(name, displayName, configure));
    }

    /// <summary>
    /// Adds ai suite.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">The configure.</param>
    public static CrestAppsCoreBuilder AddAISuite(this CrestAppsCoreBuilder builder, Action<CrestAppsAISuiteBuilder> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddCoreServices()
            .AddCoreAIServices()
            .AddCoreAIOrchestration();

        if (configure is not null)
        {
            configure(new CrestAppsAISuiteBuilder(builder.Services));
        }

        return builder;
    }

    /// <summary>
    /// Adds a core AI completion client and its registration metadata.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="clientName">The client name.</param>
    /// <param name="configure">The configuration action.</param>
    public static IServiceCollection AddCoreAICompletionClient<TClient>(this IServiceCollection services, string clientName, Action<AICompletionClientEntry> configure = null)
        where TClient : class, IAICompletionClient
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(clientName);

        return services
                    .Configure<AIOptions>(o =>
                    {
                        o.AddCompletionClient(clientName, configure);
                    })
                    .AddCoreAICompletionClient<TClient>(clientName);
    }

    /// <summary>
    /// Adds core ai deployment provider.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="clientName">The client name.</param>
    /// <param name="configure">The configure.</param>
    public static IServiceCollection AddCoreAIDeploymentProvider(this IServiceCollection services, string clientName, Action<AIDeploymentProviderEntry> configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(clientName);

        services
            .Configure<AIOptions>(o =>
            {
                o.AddDeploymentProvider(clientName, configure);
            });

        return services;
    }

    /// <summary>
    /// Adds a core AI completion client.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="clientName">The client name.</param>
    public static IServiceCollection AddCoreAICompletionClient<TClient>(
        this IServiceCollection services,
        string clientName)
        where TClient : class, IAICompletionClient
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(clientName);

        services.Configure<AIOptions>(o =>
        {
            o.AddClient<TClient>(clientName);
        });

        services.TryAddScoped<TClient>();
        services.AddScoped<IAICompletionClient>(sp => sp.GetService<TClient>());

        return services;
    }

    /// <summary>
    /// Adds core ai connection source.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="clientName">The client name.</param>
    /// <param name="configure">The configure.</param>
    public static IServiceCollection AddCoreAIConnectionSource(this IServiceCollection services, string clientName, Action<AIProviderConnectionOptionsEntry> configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(clientName);

        services.Configure<AIOptions>(o =>
        {
            o.AddConnectionSource(clientName, configure);
        });

        return services;
    }

    /// <summary>
    /// Adds core ai template source.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="sourceName">The source name.</param>
    /// <param name="configure">The configure.</param>
    public static IServiceCollection AddCoreAITemplateSource(this IServiceCollection services, string sourceName, Action<AITemplateSourceEntry> configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(sourceName);

        services.Configure<AIOptions>(o =>
        {
            o.AddTemplateSource(sourceName, configure);
        });

        return services;
    }

    /// <summary>
    /// Adds core ai data source rag.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIDataSourceRag(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<AIDataSourceIndexingQueue>();
        services.TryAddSingleton<IAIDataSourceIndexingQueue>(sp => sp.GetRequiredService<AIDataSourceIndexingQueue>());
        services.TryAddScoped<IAIDataSourceChangeNotifier, DefaultAIDataSourceChangeNotifier>();
        services.TryAddScoped<IAIDataSourceIndexingService, DefaultAIDataSourceIndexingService>();
        services.TryAddKeyedScoped<IAIDataSourceSourceHandler>(AIDataSourceSourceTypes.SearchIndexProfile, (sp, _)
            => new SearchIndexProfileAIDataSourceSourceHandler(sp.GetRequiredService<ISearchIndexProfileManager>(), sp));
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICatalogEntryHandler<AIDataSource>, AIDataSourceCatalogHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ISearchDocumentHandler, AIDataSourceSearchDocumentHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AIDataSourceIndexingBackgroundService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AIDataSourceAlignmentBackgroundService>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOrchestrationContextBuilderHandler, DataSourceOrchestrationHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPreemptiveRagHandler, DataSourcePreemptiveRagHandler>());
        services.Configure<AIDataSourceSourceOptions>(options => options.AddOrUpdate(
            AIDataSourceSourceTypes.SearchIndexProfile,
            new LocalizedString("Search Index Profile", "Search Index Profile"),
            new LocalizedString("Search Index Profile Description", "Read source documents from a CrestApps search index profile managed by the framework.")));
        services.AddCoreAITool<DataSourceSearchTool>(DataSourceSearchTool.TheName)
            .WithPurpose(AIToolPurposes.DataSourceSearch);

        return services;
    }

    /// <summary>
    /// Adds core ai memory.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIMemory(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddCatalogManagers();
        services.AddOptions<AIMemoryOptions>();
        services.AddOptions<GeneralAIOptions>();
        services.AddOptions<ChatInteractionMemoryOptions>();
        services.TryAddScoped<AIMemoryIndexingService>();
        services.TryAddScoped<IAIMemorySafetyService, DefaultAIMemorySafetyService>();
        services.TryAddScoped<IAIMemorySearchService, AIMemorySearchService>();
        services.TryAdd(ServiceDescriptor.Scoped<ICatalog<AIMemoryEntry>>(sp => sp.GetRequiredService<IAIMemoryStore>()));
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOrchestrationContextBuilderHandler, AIMemoryOrchestrationHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPreemptiveRagHandler, AIMemoryPreemptiveRagHandler>());

        services.AddCoreAITool<SearchUserMemoriesTool>(SearchUserMemoriesTool.TheName)
            .WithTitle("Search User Memories")
            .WithDescription("Search the current authenticated user's long-term memory for relevant preferences, active projects, recurring topics, interests, identity details, and other reusable background facts saved from prior conversations.")
            .WithPurpose(AIToolPurposes.Memory);

        services.AddCoreAITool<ListUserMemoriesTool>(ListUserMemoriesTool.TheName)
            .WithTitle("List User Memories")
            .WithDescription("List the current authenticated user's saved long-term memories when you need to review what durable preferences, projects, topics, interests, and other background facts are already known about them.")
            .WithPurpose(AIToolPurposes.Memory);

        services.AddCoreAITool<SaveUserMemoryTool>(SaveUserMemoryTool.TheName)
            .WithTitle("Save User Memory")
            .WithDescription("Create or update a long-term memory for the current authenticated user when they reveal durable context such as preferences, active projects, recurring topics, interests, or other facts that should persist across future conversations, even if they did not explicitly ask to save it.")
            .WithPurpose(AIToolPurposes.Memory);

        services.AddCoreAITool<RemoveUserMemoryTool>(RemoveUserMemoryTool.TheName)
            .WithTitle("Remove User Memory")
            .WithDescription("Remove a previously saved long-term memory for the current authenticated user when the user asks to forget it or when the memory should no longer be retained.")
            .WithPurpose(AIToolPurposes.Memory);

        return services;
    }

    /// <summary>
    /// Adds ai memory.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">The configure.</param>
    public static CrestAppsAISuiteBuilder AddAIMemory(this CrestAppsAISuiteBuilder builder, Action<CrestAppsAIMemoryBuilder> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIMemory();

        if (configure is not null)
        {
            configure(new CrestAppsAIMemoryBuilder(builder.Services));
        }

        return builder;
    }

    /// <summary>
    /// Adds the AI tool instances feature: parameterized, user-configured tools built from
    /// developer-defined <see cref="IAIToolInstanceSource"/> blueprints. Registers the core services and,
    /// by default, the built-in registry provider that surfaces configured instances to every orchestrator.
    /// Use the supplied builder to register one or more sources with
    /// <c>AddSource&lt;TSource&gt;(name, configure)</c> or the built-in HTTP source with
    /// <c>AddHttpApiRequestSource()</c>, and to register the persistence stores with
    /// <c>AddYesSqlStores()</c> or <c>AddEntityCoreStores()</c>.
    /// </summary>
    /// <param name="builder">The AI suite builder.</param>
    /// <param name="configure">An optional delegate used to register tool instance sources and stores.</param>
    /// <param name="useDefaultRegistry">
    /// When <see langword="true"/> (the default), registers the built-in
    /// <see cref="ToolInstanceRegistryProvider"/>. Pass <see langword="false"/> to supply your own
    /// <see cref="IToolRegistryProvider"/> instead — for example to enforce per-user permissions.
    /// </param>
    public static CrestAppsAISuiteBuilder AddToolInstances(
        this CrestAppsAISuiteBuilder builder,
        Action<CrestAppsAIToolInstancesBuilder> configure = null,
        bool useDefaultRegistry = true)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIToolInstances();

        if (useDefaultRegistry)
        {
            builder.Services.AddDefaultAIToolInstanceRegistryProvider();
        }

        if (configure is not null)
        {
            configure(new CrestAppsAIToolInstancesBuilder(builder.Services));
        }

        return builder;
    }

    /// <summary>
    /// Adds the orchestration services including the default progressive tool orchestrator,
    /// tool registry, orchestration context builder, and orchestrator resolver.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIOrchestration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register embedded templates from this assembly so they are available
        // regardless of the host application.
        services.AddTemplatesFromAssembly(typeof(ServiceCollectionExtensions).Assembly);

        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<OrchestratorOptions>();
        services.AddOptions<DefaultOrchestratorOptions>();

        services.AddOptions<DefaultOrchestratorSettings>();
        services.AddOptions<DefaultAIDeploymentSettings>();
        services.AddOptions<AIDataSourceOptions>();

        // Register DefaultAIOptions as a scoped service that reads from IOptionsSnapshot
        // and applies the current GeneralAIOptions value. Host applications can replace
        // this with their own implementation when they resolve settings differently.
        services.TryAddScoped(sp =>
        {
            var snapshot = sp.GetRequiredService<IOptionsSnapshot<DefaultAIOptions>>();
            var settings = sp.GetRequiredService<IOptionsMonitor<GeneralAIOptions>>();

            return snapshot.Value.ApplySiteOverrides(settings.CurrentValue);
        });
        // Register the Framework-level deployment manager.
        services.TryAddScoped<IAIDeploymentManager, DefaultAIDeploymentManager>();
        services.TryAddScoped<IAIDeploymentManager, DefaultAIDeploymentManager>();
        services.TryAddScoped<ISourceCatalogManager<AIDeployment>>(sp => (ISourceCatalogManager<AIDeployment>)sp.GetRequiredService<IAIDeploymentManager>());
        services.TryAddScoped<INamedSourceCatalogManager<AIDeployment>>(sp => sp.GetRequiredService<IAIDeploymentManager>());

        services.TryAddSingleton<IExternalChatRelayManager, ExternalChatRelayConnectionManager>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChatResponseHandler, AIChatResponseHandler>());
        services.TryAddScoped<IChatResponseHandlerResolver, DefaultChatResponseHandlerResolver>();

        services.TryAddScoped<IAIToolsService, DefaultAIToolsService>();
        services.TryAddSingleton<ITextTokenizer, LuceneTextTokenizer>();

        services.TryAddScoped<IAIToolAccessEvaluator, DefaultAIToolAccessEvaluator>();
        services.TryAddScoped<PreemptiveSearchQueryProvider>();

        services.AddPromotSecurityLayer();

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IToolRegistryProvider, SystemToolRegistryProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IToolRegistryProvider, ProfileToolRegistryProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IToolRegistryProvider, AgentToolRegistryProvider>());
        services.TryAddScoped<IToolRegistry, DefaultToolRegistry>();
        services.TryAddScoped<IToolMaterializer, DefaultToolMaterializer>();

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOrchestrationContextBuilderHandler, CompletionContextOrchestrationHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOrchestrationContextBuilderHandler, PreemptiveRagOrchestrationHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOrchestrationContextBuilderHandler, RealtimeRagGuidanceHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOrchestrationContextBuilderHandler, AIToolExecutionContextOrchestrationHandler>());

        services.TryAddScoped<CrestApps.Core.AI.Realtime.IRealtimeSessionConfigurator, DefaultRealtimeSessionConfigurator>();
        services.TryAddScoped<CrestApps.Core.AI.Realtime.IRealtimeOrchestrator, DefaultRealtimeOrchestrator>();
        services.TryAddScoped<CrestApps.Core.AI.Realtime.IRealtimeCapabilityResolver, DefaultRealtimeCapabilityResolver>();

        services.TryAddScoped<IOrchestrationContextBuilder, DefaultOrchestrationContextBuilder>();

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAICompletionServiceHandler, FunctionInvocationAICompletionServiceHandler>());

        // Registered after the tool-adding handler so it can strip tools and other options that the
        // resolved deployment does not declare support for.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAICompletionServiceHandler, ModelFeaturesAICompletionServiceHandler>());

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAICompletionContextBuilderHandler, DataSourceAICompletionContextBuilderHandler>());

        services.AddOrchestrator<DefaultOrchestrator>(DefaultOrchestrator.OrchestratorName)
            .WithTitle("Default");

        services.AddScoped<IOrchestratorResolver, DefaultOrchestratorResolver>();

        // Register content generation system tools.
        services.AddCoreAITool<GenerateImageTool>(GenerateImageTool.TheName)
            .WithTitle("Generate Image")
            .WithDescription("Generates an image from a text description using an AI image generation model.")
            .WithPurpose(AIToolPurposes.ContentGeneration);

        services.AddCoreAITool<GenerateChartTool>(GenerateChartTool.TheName)
            .WithTitle("Generate Chart")
            .WithDescription("Generates a Chart.js configuration from a data description.")
            .WithPurpose(AIToolPurposes.ContentGeneration);

        services.AddCoreAITool<CurrentDateTimeTool>(CurrentDateTimeTool.TheName)
            .WithTitle("Current Date & Time")
            .WithDescription("Returns the current date and time, optionally in a specific timezone.")
            .WithCategory("Utilities")
            .Selectable();

        return services;
    }

    /// <summary>
    /// Registers an orchestrator implementation with the given name.
    /// </summary>
    public static OrchestratorBuilder<TOrchestrator> AddOrchestrator<TOrchestrator>(this IServiceCollection services, string name)
        where TOrchestrator : class, IOrchestrator
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);

        services.TryAddScoped<TOrchestrator>();

        var entry = new OrchestratorEntry
        {
            Type = typeof(TOrchestrator),
        };

        services.Configure<OrchestratorOptions>(options =>
        {
            options.Orchestrators[name] = entry;
        });

        return new OrchestratorBuilder<TOrchestrator>(entry);
    }

    private static void AddPromotSecurityLayer(this IServiceCollection services)
    {
        // Prompt security services.
        services.AddOptions<AIChatRateLimitingOptions>();
        services.AddOptions<PromptSecurityOptions>();
        services.TryAddSingleton<PromptSecurityRiskScoringEngine>();
        services.TryAddSingleton<IChatRateLimiter, DefaultChatRateLimiter>();
        services.TryAddSingleton<IChatSessionStartRateLimiter, DefaultChatSessionStartRateLimiter>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPromptSecurityRule, SystemRoleInjectionRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPromptSecurityRule, InstructionOverrideRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPromptSecurityRule, PersonaJailbreakRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPromptSecurityRule, PrivilegeEscalationRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPromptSecurityRule, PromptLeakageRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPromptSecurityRule, IndirectPromptProbeRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPromptSecurityRule, HiddenContextDisclosureRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPromptSecurityRule, ConversationHistoryExtractionRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPromptSecurityRule, MemoryExtractionRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPromptSecurityRule, ConfigurationDisclosureRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPromptSecurityRule, ToolEnumerationRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPromptSecurityRule, AgentOrchestrationDiscoveryRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPromptSecurityRule, FunctionSchemaExtractionRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPromptSecurityRule, DataExfiltrationRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPromptSecurityRule, EncodedExfiltrationRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPromptSecurityRule, DelimiterManipulationRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPromptSecurityRule, RagDocumentInjectionRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPromptSecurityRule, AuthorityImpersonationRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPromptSecurityRule, HarmfulContentGenerationRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPromptSecurityRule, SensitiveDataProbeRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPromptSecurityRule, HypotheticalScenarioBypassRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPromptSecurityRule, OutputFormatManipulationRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPromptSecurityRule, VirtualizationAttackRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPromptSecurityRule, ContextPoisoningRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPromptSecurityRule, CompletionAttackRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPromptSecurityRule, CustomBlockedPatternsRule>());
        services.TryAddSingleton<PromptInjectionPatternDetector>();
        services.TryAddScoped<IPromptSecurityService, DefaultPromptSecurityService>();
        services.TryAddScoped<IOutputSecurityFilter, DefaultOutputSecurityFilter>();
        services.TryAddScoped<IAIChatSecurityAuditService, DefaultAIChatSecurityAuditService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOrchestrationContextBuilderHandler, SecurityPromptOrchestrationHandler>());
    }
}
