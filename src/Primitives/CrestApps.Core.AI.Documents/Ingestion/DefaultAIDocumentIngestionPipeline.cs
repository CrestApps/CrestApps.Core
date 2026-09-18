using Microsoft.Extensions.DataIngestion;

namespace CrestApps.Core.AI.Documents.Ingestion;

/// <summary>
/// The default pipeline: one reader, then every registered processor in registration order.
/// </summary>
/// <remarks>
/// The pipeline does not catch what a processor throws. Callers already decide what a failed read means —
/// the chat upload path, for instance, wraps the call and reports a failed document — and swallowing an
/// exception here would hide a broken processor behind a silently thinner document.
/// </remarks>
public sealed class DefaultAIDocumentIngestionPipeline : IAIDocumentIngestionPipeline
{
    private readonly IIngestionDocumentReaderResolver _readerResolver;
    private readonly IEnumerable<IngestionDocumentProcessor> _processors;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAIDocumentIngestionPipeline"/> class.
    /// </summary>
    /// <param name="readerResolver">The reader resolver.</param>
    /// <param name="processors">The processors, in registration order.</param>
    public DefaultAIDocumentIngestionPipeline(
        IIngestionDocumentReaderResolver readerResolver,
        IEnumerable<IngestionDocumentProcessor> processors)
    {
        _readerResolver = readerResolver;
        _processors = processors;
    }

    /// <summary>
    /// Reads the content and runs the registered processors over the result.
    /// </summary>
    /// <param name="content">The content to read.</param>
    /// <param name="fileName">The file name, including its extension.</param>
    /// <param name="mediaType">The media type of the content.</param>
    /// <param name="context">The per-run options handed to every processor.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The read and processed document.</returns>
    public async Task<IngestionDocument> IngestAsync(
        Stream content,
        string fileName,
        string mediaType,
        DocumentIngestionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        context ??= DocumentIngestionContext.Default;

        var reader = _readerResolver.Resolve(fileName, mediaType, content);

        if (reader == null)
        {
            throw new NotSupportedException($"No document reader registered for extension '{Path.GetExtension(fileName)}'.");
        }

        var document = await reader.ReadAsync(content, fileName, mediaType, cancellationToken);

        foreach (var processor in _processors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            document = processor is AIDocumentIngestionProcessor contextual
                ? await contextual.ProcessAsync(document, context, cancellationToken)
                : await processor.ProcessAsync(document, cancellationToken);
        }

        return document;
    }
}
