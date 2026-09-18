using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.Ingestion;

/// <summary>
/// Tries one reader and falls through to another when it cannot deliver.
/// </summary>
/// <remarks>
/// This is what makes a paid document-understanding service safe to depend on. The service reads structure
/// far better than any local heuristic, but it can be unconfigured, throttled, over its page limit, or simply
/// down, and none of those is a reason to reject a document. When the preferred reader fails, the fallback
/// reads the same bytes and the ingest continues with whatever structure can be inferred locally.
/// </remarks>
public sealed class FallbackIngestionDocumentReader : IngestionDocumentReader
{
    private readonly IngestionDocumentReader _preferred;
    private readonly IngestionDocumentReader _fallback;
    private readonly ILogger<FallbackIngestionDocumentReader> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FallbackIngestionDocumentReader"/> class.
    /// </summary>
    /// <param name="preferred">The reader to try first.</param>
    /// <param name="fallback">The reader used when the preferred one fails.</param>
    /// <param name="logger">The logger.</param>
    public FallbackIngestionDocumentReader(
        IngestionDocumentReader preferred,
        IngestionDocumentReader fallback,
        ILogger<FallbackIngestionDocumentReader> logger)
    {
        _preferred = preferred;
        _fallback = fallback;
        _logger = logger;
    }

    /// <summary>
    /// Reads the content with the preferred reader, falling through to the fallback when it fails.
    /// </summary>
    /// <param name="source">The content to read.</param>
    /// <param name="identifier">The document identifier.</param>
    /// <param name="mediaType">The media type of the content.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The read document.</returns>
    public override async Task<IngestionDocument> ReadAsync(
        Stream source,
        string identifier,
        string mediaType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        // The preferred reader consumes the stream, so the bytes are held for a second attempt. A document
        // small enough to upload is small enough to buffer, and re-reading a consumed stream is not an option.
        var buffered = await BufferAsync(source, cancellationToken);

        try
        {
            buffered.Position = 0;

            return await _preferred.ReadAsync(buffered, identifier, mediaType, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (NotSupportedException)
        {
            // The preferred reader does not handle this media type at all. That is a routing fact, not a
            // failure, so it is not worth a warning.
            buffered.Position = 0;

            return await _fallback.ReadAsync(buffered, identifier, mediaType, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Preferred document reader failed for '{Identifier}'. Falling back to local extraction.",
                identifier);

            buffered.Position = 0;

            return await _fallback.ReadAsync(buffered, identifier, mediaType, cancellationToken);
        }
        finally
        {
            await buffered.DisposeAsync();
        }
    }

    private static async Task<MemoryStream> BufferAsync(Stream source, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();

        if (source.CanSeek)
        {
            source.Position = 0;
        }

        await source.CopyToAsync(buffer, cancellationToken);

        return buffer;
    }
}
