using Microsoft.Extensions.DataIngestion;

namespace CrestApps.Core.AI.Ingestion;

/// <summary>
/// Resolves a reader for the content, reads it, and runs every registered processor in order. Both the chat
/// upload path and the indexing path go through this one pipeline so neither drifts from the other.
/// </summary>
public interface IAIDocumentIngestionPipeline
{
    /// <summary>
    /// Reads the content and runs the registered processors over the result.
    /// </summary>
    /// <param name="content">The content to read.</param>
    /// <param name="fileName">The file name, including its extension.</param>
    /// <param name="mediaType">The media type of the content.</param>
    /// <param name="context">The per-run options handed to every processor.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The read and processed document.</returns>
    Task<IngestionDocument> IngestAsync(
        Stream content,
        string fileName,
        string mediaType,
        DocumentIngestionContext context,
        CancellationToken cancellationToken = default);
}
