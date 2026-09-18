using System.Security.Cryptography;
using Azure;
using Azure.AI.DocumentIntelligence;
using CrestApps.Core.AI.Ingestion;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Azure.DocumentIntelligence.Services;

/// <summary>
/// Reads a document's structure from Azure AI Document Intelligence instead of inferring it locally.
/// </summary>
/// <remarks>
/// The layout model reports which paragraph is a heading, which is a running head, what order the page is
/// read in, where the tables are and what their cells span, and which caption belongs to which figure — for
/// any language. Everything downstream of the reader is unchanged; it simply receives facts where it used to
/// receive guesses.
/// <para>
/// Anything that goes wrong here is allowed to throw. The reader is wrapped in a
/// <see cref="FallbackIngestionDocumentReader"/>, which catches it and lets the local reader produce the
/// document instead, so a document is never rejected because a paid service was unavailable.
/// </para>
/// </remarks>
public sealed class DocumentIntelligenceIngestionDocumentReader : IngestionDocumentReader
{
    private readonly DocumentIntelligenceClient _client;
    private readonly IOptions<DocumentIntelligenceOptions> _options;
    private readonly ILogger<DocumentIntelligenceIngestionDocumentReader> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentIntelligenceIngestionDocumentReader"/> class.
    /// </summary>
    /// <param name="client">The service client.</param>
    /// <param name="options">The reader options.</param>
    /// <param name="logger">The logger.</param>
    public DocumentIntelligenceIngestionDocumentReader(
        DocumentIntelligenceClient client,
        IOptions<DocumentIntelligenceOptions> options,
        ILogger<DocumentIntelligenceIngestionDocumentReader> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Analyzes the content and maps the reported structure onto an ingestion document.
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
        ArgumentException.ThrowIfNullOrEmpty(identifier);

        var options = _options.Value;

        if (!options.IsConfigured())
        {
            throw new NotSupportedException("Azure AI Document Intelligence is not configured.");
        }

        var content = await ReadAllBytesAsync(source, cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (options.Timeout > TimeSpan.Zero)
        {
            timeout.CancelAfter(options.Timeout);
        }

        var request = new AnalyzeDocumentOptions(options.ModelId, BinaryData.FromBytes(content));

        if (options.IncludeFigureContent)
        {
            request.Output.Add(AnalyzeOutputOption.Figures);
        }

        var operation = await _client.AnalyzeDocumentAsync(WaitUntil.Completed, request, timeout.Token);
        var result = operation.Value;

        if (options.MaxPages > 0 && result.Pages != null && result.Pages.Count > options.MaxPages)
        {
            throw new NotSupportedException(
                $"The document has {result.Pages.Count} pages, above the configured maximum of {options.MaxPages}.");
        }

        var document = DocumentIntelligenceDocumentMapper.Map(result, identifier);

        if (options.IncludeFigureContent)
        {
            await AddFigureContentAsync(document, options.ModelId, operation.Id, identifier, timeout.Token);
        }

        return document;
    }

    /// <summary>
    /// Downloads the image of every figure the service found.
    /// </summary>
    /// <param name="document">The mapped document.</param>
    /// <param name="modelId">The model the document was analyzed with.</param>
    /// <param name="resultId">The analysis result the figures belong to.</param>
    /// <param name="identifier">The document identifier, used only for logging.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>
    /// A figure that cannot be downloaded keeps its caption and loses only its bytes. That degrades to the
    /// caption-only tier, which is a worse figure rather than a failed document.
    /// </remarks>
    private async Task AddFigureContentAsync(
        IngestionDocument document,
        string modelId,
        string resultId,
        string identifier,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(resultId))
        {
            return;
        }

        foreach (var element in document.EnumerateContent())
        {
            if (element is not IngestionDocumentImage image)
            {
                continue;
            }

            var providerFigureId = image.GetMetadataString(DocumentIntelligenceDocumentMapper.ProviderFigureIdKey);

            if (string.IsNullOrEmpty(providerFigureId))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var response = await _client.GetAnalyzeResultFigureAsync(modelId, resultId, providerFigureId, cancellationToken);
                var bytes = response.Value.ToArray();

                if (bytes.Length == 0)
                {
                    continue;
                }

                image.Content = bytes;
                image.MediaType = "image/png";
                image.Metadata[FigureMetadataKeys.ContentHash] = Convert.ToHexStringLower(SHA256.HashData(bytes));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not download figure '{FigureId}' of '{Identifier}'. The figure keeps its caption.",
                    providerFigureId,
                    identifier);
            }
        }
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream source, CancellationToken cancellationToken)
    {
        if (source is MemoryStream memory)
        {
            return memory.ToArray();
        }

        if (source.CanSeek)
        {
            source.Position = 0;
        }

        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken);

        return buffer.ToArray();
    }
}
