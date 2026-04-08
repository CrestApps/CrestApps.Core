using CrestApps.Core.AI.Chat.Handlers;
using CrestApps.Core.AI.Chat.Services;
using CrestApps.Core.AI.Chat.Tools;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Builders;
using CrestApps.Core.Services;
using CrestApps.Core.Templates.Extensions;
using Fluid;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestApps.Core.AI.Chat;

/// <summary>
/// Extension methods for registering chat and document processing services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the default chat notification sender and built-in notification action handlers.
    /// The sender dispatches notifications to keyed <see cref="IChatNotificationTransport"/>
    /// implementations, which must be registered separately by each host (OrchardCore, MVC, etc.).
    /// </summary>
    public static IServiceCollection AddCoreAIChatNotifications(this IServiceCollection services)
    {
        services.TryAddScoped<IChatNotificationSender, DefaultChatNotificationSender>();
        services.TryAddKeyedScoped<IChatNotificationActionHandler, CancelTransferNotificationActionHandler>(ChatNotificationActionNames.CancelTransfer);
        services.TryAddKeyedScoped<IChatNotificationActionHandler, EndSessionNotificationActionHandler>(ChatNotificationActionNames.EndSession);

        return services;
    }

    /// <summary>
    /// Configures standard hub options (timeouts, message sizes) for a chat hub.
    /// Call this for each concrete hub type that handles AI chat traffic.
    /// </summary>
    public static IServiceCollection ConfigureCrestAppsChatHubOptions<THub>(this IServiceCollection services) where THub : Hub
    {
        services.Configure<HubOptions<THub>>(options =>
        {
            // Allow long-running operations (e.g., multi-step MCP tool calls)
            // without the server dropping the connection prematurely.
            options.ClientTimeoutInterval = TimeSpan.FromMinutes(10);
            options.KeepAliveInterval = TimeSpan.FromSeconds(15);

            // Allow larger messages for audio transcription payloads.
            options.MaximumReceiveMessageSize = 10 * 1024 * 1024;
        });

        return services;
    }

    /// <summary>
    /// Adds shared chat-session processing services used by both AI profile chat
    /// and chat interactions across hosts.
    /// </summary>
    public static IServiceCollection AddCoreAIChatSessionProcessing(this IServiceCollection services)
    {
        services.TryAddScoped<DataExtractionService>();
        services.TryAddScoped<PostSessionProcessingService>();
        services.TryAddScoped<AIChatSessionPostCloseProcessor>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOrchestrationContextBuilderHandler, ExtractedDataOrchestrationHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIChatSessionHandler, DataExtractionChatSessionHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIChatSessionHandler, PostSessionProcessingChatSessionHandler>());

        services.Configure<TemplateOptions>(o =>
        {
            o.MemberAccessStrategy.Register<ExtractedFieldChange>();
        });

        return services;
    }

    /// <summary>
    /// Adds the default chat interaction handlers.
    /// </summary>
    public static IServiceCollection AddCoreAIChatInteractions(this IServiceCollection services)
    {
        services.AddCoreAIChatNotifications();
        services.AddCoreAIChatSessionProcessing();

        // Register templates embedded in this assembly.
        services.AddTemplatesFromAssembly(typeof(ServiceCollectionExtensions).Assembly);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAICompletionContextBuilderHandler, ChatInteractionCompletionContextBuilderHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChatInteractionSettingsHandler, DataSourceChatInteractionSettingsHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChatInteractionSettingsHandler, PromptTemplateChatInteractionSettingsHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICatalogEntryHandler<ChatInteraction>, ChatInteractionEntryHandler>());

        return services;
    }

    public static CrestAppsAISuiteBuilder AddChatInteractions(this CrestAppsAISuiteBuilder builder, Action<CrestAppsChatInteractionsBuilder> configure = null)
    {
        builder.Services.AddCoreAIChatInteractions();

        if (configure is not null)
        {
            configure(new CrestAppsChatInteractionsBuilder(builder.Services));
        }

        return builder;
    }

    public static CrestAppsChatInteractionsBuilder ConfigureChatHubOptions<THub>(this CrestAppsChatInteractionsBuilder builder) where THub : Hub
    {
        builder.Services.ConfigureCrestAppsChatHubOptions<THub>();
        return builder;
    }

    /// <summary>
    /// Adds the default document processing system tools and supporting services.
    /// </summary>
    public static IServiceCollection AddCoreAIDocumentProcessing(this IServiceCollection services)
    {
        services.TryAddSingleton<IAITextNormalizer, DefaultAITextNormalizer>();
        services.TryAddScoped<IAIDocumentProcessingService, DefaultAIDocumentProcessingService>();

        // Register the tabular batch processor (used by heavy processing tools)
        services.TryAddScoped<ITabularBatchProcessor, TabularBatchProcessor>();

        // Register the tabular batch result cache (uses IDistributedCache)
        services.TryAddSingleton<ITabularBatchResultCache, TabularBatchResultCache>();

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOrchestrationContextBuilderHandler, DocumentOrchestrationHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPreemptiveRagHandler, DocumentPreemptiveRagHandler>());

        services.AddCoreAIIngestionDocumentReader<PlainTextIngestionDocumentReader>(
            ".txt",
            new ExtractorExtension(".csv", false),
            ".md",
            ".json",
            ".xml",
            ".html",
            ".htm",
            ".log",
            ".yaml",
            ".yml");

        // Register document system tools (available when documents are attached).
        services.AddCoreAITool<SearchDocumentsTool>(SearchDocumentsTool.TheName)
            .WithTitle("Search Documents")
            .WithDescription("Searches uploaded or attached documents using semantic vector search.")
            .WithPurpose(AIToolPurposes.DocumentProcessing);

        services.AddCoreAITool<ReadDocumentTool>(ReadDocumentTool.TheName)
            .WithTitle("Read Document")
            .WithDescription("Reads the full text content of a specific document.")
            .WithPurpose(AIToolPurposes.DocumentProcessing);

        services.AddCoreAITool<ReadTabularDataTool>(ReadTabularDataTool.TheName)
            .WithTitle("Read Tabular Data")
            .WithDescription("Reads and parses tabular data (CSV, TSV, Excel) from a document.")
            .WithPurpose(AIToolPurposes.DocumentProcessing);

        return services;
    }

    public static CrestAppsAISuiteBuilder AddDocumentProcessing(this CrestAppsAISuiteBuilder builder, Action<CrestAppsDocumentProcessingBuilder> configure = null)
    {
        builder.Services.AddCoreAIDocumentProcessing();

        if (configure is not null)
        {
            configure(new CrestAppsDocumentProcessingBuilder(builder.Services));
        }

        return builder;
    }

    [Obsolete("Use AddAISuite(ai => ai.AddDocumentProcessing(...)).")]
    public static CrestAppsCoreBuilder AddDocumentProcessing(this CrestAppsCoreBuilder builder, Action<CrestAppsDocumentProcessingBuilder> configure = null)
    {
        builder.Services.AddCoreAIDocumentProcessing();

        if (configure is not null)
        {
            configure(new CrestAppsDocumentProcessingBuilder(builder.Services));
        }

        return builder;
    }

}
