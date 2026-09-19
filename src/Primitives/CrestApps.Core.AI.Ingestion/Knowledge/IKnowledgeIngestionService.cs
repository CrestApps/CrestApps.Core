using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Ingestion.Knowledge;

/// <summary>
/// Turns a file into the typed knowledge objects of one AI data source. Manual uploads and file sources both go
/// through here, so both produce exactly the same knowledge.
/// </summary>
public interface IKnowledgeIngestionService
{
    /// <summary>
    /// Ingests one file.
    /// </summary>
    /// <param name="dataSource">The data source to ingest into.</param>
    /// <param name="content">The file content.</param>
    /// <param name="fileName">The file name.</param>
    /// <param name="mediaType">The media type of the content.</param>
    /// <param name="options">The per-run options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the ingest produced.</returns>
    Task<KnowledgeIngestionResult> IngestAsync(
        AIDataSource dataSource,
        Stream content,
        string fileName,
        string mediaType,
        KnowledgeIngestionOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an ingested document and everything it produced.
    /// </summary>
    /// <param name="dataSource">The data source the document belongs to.</param>
    /// <param name="rootId">The document's canonical identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task RemoveAsync(AIDataSource dataSource, string rootId, CancellationToken cancellationToken = default);
}
