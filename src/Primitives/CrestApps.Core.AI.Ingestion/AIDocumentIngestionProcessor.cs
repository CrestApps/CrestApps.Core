using Microsoft.Extensions.DataIngestion;

namespace CrestApps.Core.AI.Ingestion;

/// <summary>
/// The base class for a processor that enriches an <see cref="IngestionDocument"/> in place and needs the
/// per-run options an ingestion produces. The library's own overload stays callable and runs with
/// <see cref="DocumentIngestionContext.Default"/>.
/// </summary>
public abstract class AIDocumentIngestionProcessor : IngestionDocumentProcessor
{
    /// <summary>
    /// Processes the document with the default context.
    /// </summary>
    /// <param name="document">The document to process.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The processed document.</returns>
    public sealed override Task<IngestionDocument> ProcessAsync(IngestionDocument document, CancellationToken cancellationToken = default)
    {
        return ProcessAsync(document, DocumentIngestionContext.Default, cancellationToken);
    }

    /// <summary>
    /// Processes the document with the options for this run.
    /// </summary>
    /// <param name="document">The document to process.</param>
    /// <param name="context">The per-run options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The processed document.</returns>
    public abstract Task<IngestionDocument> ProcessAsync(
        IngestionDocument document,
        DocumentIngestionContext context,
        CancellationToken cancellationToken = default);
}
