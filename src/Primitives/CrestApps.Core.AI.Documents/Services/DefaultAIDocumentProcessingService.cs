using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Ingestion;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Services;

/// <summary>
/// Default host-agnostic document processor used by MVC and Orchard Core hosts.
/// </summary>
public sealed class DefaultAIDocumentProcessingService : IAIDocumentProcessingService
{
    private const int MaxEmbeddingTotalChars = 25000;
    private const int MaxStoredChunkLength = 16000;
    private const string FigureBlockStart = "[figure";
    private const string FigureBlockEnd = "[/figure]";

    private readonly IAIDocumentIngestionPipeline _pipeline;
    private readonly IAITextNormalizer _textNormalizer;
    private readonly IDocumentFileStore _fileStore;
    private readonly IOptions<ChatDocumentsOptions> _extractorOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DefaultAIDocumentProcessingService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAIDocumentProcessingService"/> class.
    /// </summary>
    /// <param name="pipeline">The ingestion pipeline that reads the file and enriches it.</param>
    /// <param name="textNormalizer">The text normalizer.</param>
    /// <param name="fileStore">The store the figure bytes are written to.</param>
    /// <param name="extractorOptions">The extractor options.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public DefaultAIDocumentProcessingService(
        IAIDocumentIngestionPipeline pipeline,
        IAITextNormalizer textNormalizer,
        IDocumentFileStore fileStore,
        IOptions<ChatDocumentsOptions> extractorOptions,
        TimeProvider timeProvider,
        ILogger<DefaultAIDocumentProcessingService> logger)
    {
        _pipeline = pipeline;
        _textNormalizer = textNormalizer;
        _fileStore = fileStore;
        _extractorOptions = extractorOptions;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Processs file.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="referenceId">The reference id.</param>
    /// <param name="referenceType">The reference type.</param>
    /// <param name="embeddingGenerator">The embedding generator.</param>
    public async Task<DocumentProcessingResult> ProcessFileAsync(
        IFormFile file,
        string referenceId,
        string referenceType,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrEmpty(referenceId);
        ArgumentException.ThrowIfNullOrEmpty(referenceType);

        var extension = Path.GetExtension(file.FileName);
        var options = _extractorOptions.Value;
        var isTabular = options.IsTabularFileExtension(extension);

        string text;
        IngestionDocument ingestionDoc;
        try
        {
            using var stream = file.OpenReadStream();
            var mediaType = MediaTypeHelper.InferMediaType(extension, file.ContentType);
            ingestionDoc = await _pipeline.IngestAsync(
                stream,
                file.FileName,
                mediaType,
                new DocumentIngestionContext
                {
                    FigureMode = options.AnalyzeImagesAtUpload ? FigureProcessingMode.Auto : FigureProcessingMode.Off,
                    DescribeFiguresInline = true,
                });

            text = FlattenForEmbedding(ingestionDoc);
        }
        catch (NotSupportedException ex)
        {
            // The pipeline could not read this file at all: no reader is registered for it, or the reader it
            // found rejected the media type. That is a statement about the file, not a fault, so the reason
            // is reported verbatim rather than flattened into a generic failure.
            _logger.LogWarning(ex, "Document processing: cannot read file '{FileName}' with extension '{Extension}'.", file.FileName, extension);

            return DocumentProcessingResult.Failed(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document processing: failed to read file '{FileName}' with extension '{Extension}'.", file.FileName, extension);

            return DocumentProcessingResult.Failed($"Failed to read the document '{file.FileName}'.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Document processing: no text content extracted from file '{FileName}'.", file.FileName);
            }

            return DocumentProcessingResult.Failed("Could not extract text content from the document.");
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Document processing: extracted {TextLength} chars from '{FileName}' (extension: {Extension}).",
                text.Length,
                file.FileName,
                extension);
        }

        var textChunks = isTabular
            ? ChunkRawContent(text)
            : RepeatFigureHeaders(await NormalizeDocumentChunksAsync(text));

        if (textChunks.Count == 0)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Document processing: no normalized text content remained for file '{FileName}'.", file.FileName);
            }

            return DocumentProcessingResult.Failed("Could not extract text content from the document.");
        }

        if (!isTabular)
        {
            text = string.Join('\n', textChunks);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var document = new AIDocument
        {
            ItemId = UniqueId.GenerateId(),
            ReferenceId = referenceId,
            ReferenceType = referenceType,
            FileName = file.FileName,
            ContentType = file.ContentType,
            FileSize = file.Length,
            UploadedUtc = now,
        };

        await StoreFiguresAsync(ingestionDoc, document, referenceId, referenceType, file.FileName);

        var chunks = new List<AIDocumentChunk>();
        GeneratedEmbeddings<Embedding<float>> embeddings = null;
        var embeddedChunkCount = 0;

        if (ShouldGenerateEmbeddings(extension, text.Length, embeddingGenerator, options))
        {
            var chunksForEmbedding = LimitChunksForEmbedding(textChunks);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Document processing: generated {ChunkCount} chunk(s) for '{FileName}'.", textChunks.Count, file.FileName);
            }

            if (embeddingGenerator != null && chunksForEmbedding.Count > 0)
            {
                try
                {
                    embeddings = await embeddingGenerator.GenerateAsync(chunksForEmbedding);
                    embeddedChunkCount = chunksForEmbedding.Count;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to generate embeddings for '{FileName}'. Chunks will be stored without embeddings.", file.FileName);
                }
            }
        }
        else if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Document processing: skipping embedding generation for '{FileName}' (extension={Extension}, textLength={TextLength}, hasGenerator={HasGenerator}).",
                file.FileName,
                extension,
                text.Length,
                embeddingGenerator != null);
        }

        for (var i = 0; i < textChunks.Count; i++)
        {
            chunks.Add(new AIDocumentChunk
            {
                ItemId = UniqueId.GenerateId(),
                AIDocumentId = document.ItemId,
                ReferenceId = referenceId,
                ReferenceType = referenceType,
                Content = textChunks[i],
                Embedding = embeddings != null && i < embeddedChunkCount && i < embeddings.Count
                    ? embeddings[i].Vector.ToArray()
                    : null,
                Index = i,
            });
        }

        var documentInfo = new ChatDocumentInfo
        {
            DocumentId = document.ItemId,
            FileName = document.FileName,
            FileSize = document.FileSize,
            ContentType = document.ContentType,
        };

        return DocumentProcessingResult.Succeeded(document, documentInfo, chunks);
    }

    private async Task<List<string>> NormalizeDocumentChunksAsync(string text)
    {
        return await _textNormalizer.NormalizeAndChunkAsync(text);
    }

    /// <summary>
    /// Flattens the ingested document into the text that gets chunked and embedded.
    /// </summary>
    /// <param name="document">The ingested document.</param>
    /// <returns>The flattened text.</returns>
    private static string FlattenForEmbedding(IngestionDocument document)
    {
        var parts = new List<string>();

        foreach (var element in document.EnumerateContent())
        {
            // A caption is emitted with the figure it belongs to, never again on its own. Page furniture is
            // kept in the document for structure analysis and is never text worth embedding.
            if (element.GetMetadataString(ElementMetadataKeys.IsCaptionFor) != null || element.IsDecoration())
            {
                continue;
            }

            if (element is IngestionDocumentImage image)
            {
                var block = RenderFigureBlock(image);

                if (block != null)
                {
                    parts.Add(block);
                }

                continue;
            }

            var text = element.GetSemanticText();

            if (!string.IsNullOrWhiteSpace(text))
            {
                parts.Add(text);
            }
        }

        return string.Join('\n', parts);
    }

    /// <summary>
    /// Renders one figure as a fenced block. The fence exists so a chunk can never lose the association
    /// between a description and the figure it describes: whatever a chunker does to the text around it, the
    /// identifier, the page and the caption travel with the description.
    /// </summary>
    /// <param name="image">The figure.</param>
    /// <returns>The block, or <see langword="null"/> when nothing is known about the figure.</returns>
    private static string RenderFigureBlock(IngestionDocumentImage image)
    {
        var caption = image.GetMetadataString(FigureMetadataKeys.Caption);
        var description = image.AlternativeText;

        if (string.IsNullOrWhiteSpace(caption) && string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.Append(BuildFigureHeader(image.GetFigureId(), image.PageNumber, caption));

        if (!string.IsNullOrWhiteSpace(description))
        {
            builder.Append('\n');
            builder.Append(description);
        }

        builder.Append('\n');
        builder.Append(FigureBlockEnd);

        return builder.ToString();
    }

    private static string BuildFigureHeader(string figureId, int? page, string caption)
    {
        var builder = new StringBuilder(FigureBlockStart);

        builder.Append(' ');
        builder.Append(figureId);

        if (page.HasValue)
        {
            builder.Append(" | page ");
            builder.Append(page.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(caption))
        {
            builder.Append(" | ");
            builder.Append(caption);
        }

        builder.Append(']');

        return builder.ToString();
    }

    /// <summary>
    /// Repairs figure blocks that a chunk boundary cut in half. A continuation chunk that closes a figure it
    /// never opened is meaningless on its own, so the opening line is repeated at the top of it.
    /// </summary>
    /// <param name="chunks">The chunks as the normalizer produced them.</param>
    /// <returns>The chunks, with any orphaned continuation given back its header.</returns>
    private static List<string> RepeatFigureHeaders(List<string> chunks)
    {
        string openHeader = null;

        for (var index = 0; index < chunks.Count; index++)
        {
            var chunk = chunks[index];
            var headerPosition = chunk.LastIndexOf(FigureBlockStart, StringComparison.Ordinal);
            var endPosition = chunk.LastIndexOf(FigureBlockEnd, StringComparison.Ordinal);

            if (openHeader != null && (headerPosition < 0 || endPosition < headerPosition))
            {
                chunks[index] = openHeader + "\n" + chunk;
            }

            if (headerPosition >= 0 && endPosition < headerPosition)
            {
                openHeader = ReadHeaderLine(chunk, headerPosition);

                continue;
            }

            if (endPosition >= 0)
            {
                openHeader = null;
            }
        }

        return chunks;
    }

    private static string ReadHeaderLine(string chunk, int headerPosition)
    {
        var lineEnd = chunk.IndexOf('\n', headerPosition);

        return lineEnd < 0
            ? chunk[headerPosition..]
            : chunk[headerPosition..lineEnd];
    }

    /// <summary>
    /// Stores the bytes of every figure that survived salience scoring, and records where they went on the
    /// document itself so a later tool can show the picture.
    /// </summary>
    /// <param name="ingestionDoc">The ingested document.</param>
    /// <param name="document">The stored document record.</param>
    /// <param name="referenceId">The owning reference identifier.</param>
    /// <param name="referenceType">The owning reference type.</param>
    /// <param name="fileName">The uploaded file name, used only for logging.</param>
    private async Task StoreFiguresAsync(
        IngestionDocument ingestionDoc,
        AIDocument document,
        string referenceId,
        string referenceType,
        string fileName)
    {
        if (_fileStore == null)
        {
            return;
        }

        var figures = new List<DocumentFigure>();

        foreach (var element in ingestionDoc.EnumerateContent())
        {
            if (element is not IngestionDocumentImage image || image.Content is not { } content)
            {
                continue;
            }

            // A repeat of artwork placed elsewhere in the document is the same bytes; the first placement
            // stores them once.
            if (image.GetMetadataString(FigureMetadataKeys.DuplicateOf) != null)
            {
                continue;
            }

            var figureId = image.GetFigureId();

            if (string.IsNullOrEmpty(figureId))
            {
                continue;
            }

            var mediaType = image.MediaType ?? "image/png";
            var storedName = $"{figureId}{(mediaType == "image/jpeg" ? ".jpg" : ".png")}";

            try
            {
                var path = DocumentFileStoragePath.Create(referenceType, referenceId, storedName, "figures");
                await using var stream = new MemoryStream(content.ToArray());
                await _fileStore.SaveFileAsync(path.StoragePath, stream);

                image.Metadata[FigureMetadataKeys.StoragePath] = path.StoragePath;

                figures.Add(new DocumentFigure
                {
                    FigureId = figureId,
                    Page = image.PageNumber,
                    Caption = image.GetMetadataString(FigureMetadataKeys.Caption),
                    StoragePath = path.StoragePath,
                    MediaType = mediaType,
                });
            }
            catch (Exception ex)
            {
                // Storing the picture is a convenience for a later tool. Failing to store it must never cost
                // the text that was already extracted from the document.
                _logger.LogWarning(ex, "Document processing: failed to store figure '{FigureId}' from '{FileName}'.", figureId, fileName);
            }
        }

        if (figures.Count > 0)
        {
            document.Put(new DocumentFigureList
            {
                Figures = figures,
            });
        }
    }

    private static List<string> ChunkRawContent(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var chunks = new List<string>();

        for (var start = 0; start < text.Length; start += MaxStoredChunkLength)
        {
            var length = Math.Min(MaxStoredChunkLength, text.Length - start);
            chunks.Add(text.Substring(start, length));
        }

        return chunks;
    }

    private static bool ShouldGenerateEmbeddings(
        string extension,
        int textLength,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        ChatDocumentsOptions options)
    {
        if (embeddingGenerator == null)
        {
            return false;
        }

        if (!options.EmbeddableFileExtensions.Contains(extension))
        {
            return false;
        }

        if (textLength > MaxEmbeddingTotalChars * 2)
        {
            return false;
        }

        return true;
    }

    private static List<string> LimitChunksForEmbedding(List<string> chunks)
    {
        var limitedChunks = new List<string>();
        var totalLength = 0;

        foreach (var chunk in chunks)
        {
            if (totalLength + chunk.Length > MaxEmbeddingTotalChars)
            {
                break;
            }

            limitedChunks.Add(chunk);
            totalLength += chunk.Length;
        }

        return limitedChunks;
    }
}
