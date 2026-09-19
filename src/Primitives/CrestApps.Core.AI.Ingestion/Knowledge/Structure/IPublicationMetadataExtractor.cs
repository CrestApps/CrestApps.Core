using Microsoft.Extensions.DataIngestion;

namespace CrestApps.Core.AI.Ingestion.Knowledge.Structure;

/// <summary>
/// Reads what a document says about itself.
/// </summary>
/// <remarks>
/// A citation that names the file a document arrived in is useless to anyone who has to find the source
/// again. The publication, the issue and the date are what a reader actually needs, and they are printed on
/// the first page or two of almost every document that has them.
/// <para>
/// This costs one model call per ingest and must never fail one. Nothing found simply means a citation
/// names the file, which is what it did before.
/// </para>
/// </remarks>
public interface IPublicationMetadataExtractor
{
    /// <summary>
    /// Reads a document's front matter.
    /// </summary>
    /// <param name="document">The ingested document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the front matter said, or <see langword="null"/> when it said nothing.</returns>
    Task<PublicationMetadata> ExtractAsync(IngestionDocument document, CancellationToken cancellationToken = default);
}
