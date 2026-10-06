using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Documents.Word.Services;
using CrestApps.Core.AI.Documents.Word.Tools;
using CrestApps.Core.AI.Documents.Word.Workspace;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Builders;
using CrestApps.Core.Templates.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestApps.Core.AI.Documents.Word;

/// <summary>
/// Provides extension methods for registering the Word agent.
/// </summary>
public static class WordServiceCollectionExtensions
{
    /// <summary>
    /// Adds the system Word agent: its tools, its per-conversation workspace, the guidance that steers Word
    /// requests to it, and the handlers that clean the workspace up.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <remarks>
    /// The <c>.docx</c> writer behind generated files and tabular exports is registered by the Open XML package
    /// (<c>AddOpenXml()</c>), and is built on the same styles, numbering and table writer the agent uses. The
    /// agent can be turned off through <see cref="WordAgentOptions.Enabled"/> without losing the writer.
    /// </remarks>
    public static IServiceCollection AddCoreAIWordDocumentProcessing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<WordAgentOptions>();
        services.AddOptions<WordPreviewOptions>();

        services.TryAddSingleton<IWordWorkspaceStore, DocumentFileStoreWordWorkspaceStore>();

        services.AddTemplatesFromAssembly(typeof(WordServiceCollectionExtensions).Assembly);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIProfileProvider, WordAgentProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOrchestrationContextBuilderHandler, WordDocumentOrchestrationHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IConversationWorkspaceCleanupHandler, WordWorkspaceCleanupHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChatInteractionHistoryHandler, WordWorkspaceHistoryClearedHandler>());

        WordToolRegistrations.AddWordTools(services);

        return services;
    }

    /// <summary>
    /// Adds the system Word agent to document processing.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static CrestAppsDocumentProcessingBuilder AddWord(this CrestAppsDocumentProcessingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIWordDocumentProcessing();

        return builder;
    }
}
