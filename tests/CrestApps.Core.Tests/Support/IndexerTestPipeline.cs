using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Ingestion.Knowledge;
using CrestApps.Core.AI.Ingestion.Knowledge.Structure;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Support;

/// <summary>
/// Builds the real ingestion pipeline over in-memory stores, so an indexer test exercises the code that runs
/// in production rather than a stand-in for it.
/// </summary>
internal static class IndexerTestPipeline
{
    /// <summary>
    /// Creates the ingestion service.
    /// </summary>
    /// <param name="store">The knowledge object store to write into.</param>
    /// <param name="fileStore">The file store, or <see langword="null"/> for a fresh one.</param>
    /// <param name="queue">The indexing queue, or <see langword="null"/> for a fresh one.</param>
    /// <returns>The ingestion service.</returns>
    public static DefaultKnowledgeIngestionService Create(
        InMemoryKnowledgeObjectStore store,
        RecordingDocumentFileStore fileStore = null,
        RecordingIndexingQueue queue = null)
    {
        var services = new ServiceCollection();

        services.AddSingleton<PlainTextIngestionDocumentReader>();
        services.AddKeyedSingleton<IngestionDocumentReader>(
            "text/plain",
            (sp, _) => sp.GetRequiredService<PlainTextIngestionDocumentReader>());

        var serviceProvider = services.BuildServiceProvider();

        return new DefaultKnowledgeIngestionService(
            new DefaultAIDocumentIngestionPipeline(new DefaultIngestionDocumentReaderResolver(serviceProvider), []),
            store,
            fileStore ?? new RecordingDocumentFileStore(),
            new DefaultAITextNormalizer(),
            new TocSeededStructureAnalyzer(NullLogger<TocSeededStructureAnalyzer>.Instance),
            new NullPublicationMetadataExtractor(),
            queue ?? new RecordingIndexingQueue(),
            NullLogger<DefaultKnowledgeIngestionService>.Instance);
    }

    /// <summary>
    /// Reads nothing, so a test never depends on a model being configured.
    /// </summary>
    private sealed class NullPublicationMetadataExtractor : IPublicationMetadataExtractor
    {
        public Task<PublicationMetadata> ExtractAsync(IngestionDocument document, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PublicationMetadata>(null);
        }
    }
}
