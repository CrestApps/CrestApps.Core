using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Services;
using CrestApps.Core.AI.Documents.Pdf.Tools;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using CrestApps.Core.AI.Ingestion.Pdf;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Builders;
using CrestApps.Core.Templates.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestApps.Core.AI.Documents.Pdf;

/// <summary>
/// Provides extension methods for PDF Service Collection.
/// </summary>
public static class PdfServiceCollectionExtensions
{
    /// <summary>
    /// Adds PDF document processing: the PDF reader, the PDF writer, and the system PDF agent.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <remarks>
    /// Registers every part of PDF support: the reader, which lives on the ingestion path in
    /// <c>CrestApps.Core.AI.Ingestion.Pdf</c>; the writer that turns generated content into a downloadable
    /// PDF; and the system PDF agent with its tools, its per-conversation workspace and the handlers that
    /// clean that workspace up. A host that only reads PDFs into a knowledge base takes the ingestion package
    /// on its own and leaves the rest, and the document processing it needs, behind. The agent can be turned
    /// off through <see cref="PdfAgentOptions.Enabled"/> without losing the reader or the writer.
    /// </remarks>
    public static IServiceCollection AddCoreAIPdfDocumentProcessing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCoreAIPresentationPdfExport();
        services.AddCoreAIPdfIngestion();

        services.AddOptions<PdfCompositionOptions>();
        services.AddOptions<PdfPreviewOptions>();
        services.AddOptions<PdfAgentOptions>();

        // Register the PDF output writer so generated files can be downloaded as PDF documents.
        services.AddGeneratedFileWriter<PdfGeneratedFileWriter>(".pdf");

        services.TryAddSingleton<PdfDocumentComposer>();
        services.TryAddSingleton<IPdfWorkspaceStore, DocumentFileStorePdfWorkspaceStore>();

        services.AddTemplatesFromAssembly(typeof(PdfServiceCollectionExtensions).Assembly);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIProfileProvider, PdfAgentProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOrchestrationContextBuilderHandler, PdfDocumentOrchestrationHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IConversationWorkspaceCleanupHandler, PdfWorkspaceCleanupHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChatInteractionHistoryHandler, PdfWorkspaceHistoryClearedHandler>());

        PdfToolRegistrations.AddPdfTools(services);

        return services;
    }

    /// <summary>
    /// Adds PDF reading, writing and the system PDF agent to document processing.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static CrestAppsDocumentProcessingBuilder AddPdf(this CrestAppsDocumentProcessingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIPdfDocumentProcessing();

        return builder;
    }
}
