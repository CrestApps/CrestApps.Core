using System.Security.Cryptography;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Documents.Ingestion;
using CrestApps.Core.AI.Documents.Ingestion.Processors;
using CrestApps.Core.AI.Documents.Knowledge.Structure;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.Knowledge;

/// <summary>
/// The one way a file becomes knowledge: read it, store its figures, turn it into typed objects, and queue
/// the objects for indexing.
/// </summary>
/// <remarks>
/// Figures are never transcribed inline here. A five hundred page document would otherwise stay unsearchable
/// until every picture in it had been through a vision model; instead the text is indexed immediately and the
/// figures are transcribed afterwards by the backfill service.
/// </remarks>
public sealed class DefaultKnowledgeIngestionService : IKnowledgeIngestionService
{
    /// <summary>
    /// The folder segment stored figures live under, which is deliberately a literal rather than the data
    /// source's source type.
    /// </summary>
    /// <remarks>
    /// A source type is a name shown in the admin UI and may be renamed; a storage path is written into every
    /// stored object and read back verbatim to serve the file. Deriving one from the other would mean a
    /// rename silently splits the store into two prefixes, with older figures under the old one. They are
    /// separate concerns and this keeps them separate.
    /// </remarks>
    private const string FigureStorageSegment = "Ingested";

    private readonly IAIDocumentIngestionPipeline _pipeline;
    private readonly IKnowledgeObjectStore _store;
    private readonly IDocumentFileStore _fileStore;
    private readonly IAITextNormalizer _textNormalizer;
    private readonly IDocumentStructureAnalyzer _structureAnalyzer;
    private readonly IPublicationMetadataExtractor _publicationMetadataExtractor;
    private readonly IAIDataSourceIndexingQueue _indexingQueue;
    private readonly ILogger<DefaultKnowledgeIngestionService> _logger;

    // The service is scoped, so one instance covers one unit of work. Remembering what it has already
    // produced is what lets the same bytes arriving twice in that unit of work replace themselves, which a
    // read of the store cannot do while the writes are still uncommitted.
    private readonly Dictionary<string, IReadOnlyList<KnowledgeObject>> _producedByDocument = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultKnowledgeIngestionService"/> class.
    /// </summary>
    /// <param name="pipeline">The ingestion pipeline.</param>
    /// <param name="store">The knowledge object store.</param>
    /// <param name="fileStore">The store the figure bytes are written to.</param>
    /// <param name="textNormalizer">The text normalizer used to chunk the article text.</param>
    /// <param name="structureAnalyzer">The analyzer that works out what the document is made of.</param>
    /// <param name="publicationMetadataExtractor">The extractor that reads what the document says about itself.</param>
    /// <param name="indexingQueue">The indexing queue.</param>
    /// <param name="logger">The logger.</param>
    public DefaultKnowledgeIngestionService(
        IAIDocumentIngestionPipeline pipeline,
        IKnowledgeObjectStore store,
        IDocumentFileStore fileStore,
        IAITextNormalizer textNormalizer,
        IDocumentStructureAnalyzer structureAnalyzer,
        IPublicationMetadataExtractor publicationMetadataExtractor,
        IAIDataSourceIndexingQueue indexingQueue,
        ILogger<DefaultKnowledgeIngestionService> logger)
    {
        _pipeline = pipeline;
        _store = store;
        _fileStore = fileStore;
        _textNormalizer = textNormalizer;
        _structureAnalyzer = structureAnalyzer;
        _publicationMetadataExtractor = publicationMetadataExtractor;
        _indexingQueue = indexingQueue;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<KnowledgeIngestionResult> IngestAsync(
        AIDataSource dataSource,
        Stream content,
        string fileName,
        string mediaType,
        KnowledgeIngestionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        options ??= new KnowledgeIngestionOptions();

        var bytes = await ReadAllBytesAsync(content, cancellationToken);

        // The key comes from the bytes, so re-ingesting an identical file replaces what it produced last time
        // instead of storing a second copy of it.
        var contentHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var fileKey = contentHash[..16];
        var rootId = $"{KnowledgeContentTypes.Document}:{fileKey}";

        IngestionDocument document;

        try
        {
            await using var source = new MemoryStream(bytes);

            document = await _pipeline.IngestAsync(
                source,
                fileName,
                mediaType,
                new DocumentIngestionContext
                {
                    FigureMode = options.FigureMode,
                    VisionDeploymentName = options.VisionDeploymentName,
                    MaxFigureDescriptionsPerDocument = options.MaxFigureDescriptionsPerDocument,
                    Language = options.Language,
                    DescribeFiguresInline = false,
                    DataSourceId = dataSource.ItemId,
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read '{FileName}' for data source '{DataSourceId}'.", fileName, dataSource.ItemId);

            return new KnowledgeIngestionResult
            {
                Success = false,
                Error = $"Failed to read '{fileName}'.",
            };
        }

        await StoreFiguresAsync(document, dataSource.ItemId, fileName, cancellationToken);

        // Analysis stamps every element with the article it belongs to, so the text has to be collected
        // after it rather than before.
        var structure = _structureAnalyzer.Analyze(document);
        var chunksByArticle = new Dictionary<int, IReadOnlyList<string>>();

        foreach (var article in structure.Articles)
        {
            var articleText = BuildArticleText(document, article.Ordinal);

            chunksByArticle[article.Ordinal] = await _textNormalizer.NormalizeAndChunkAsync(articleText, cancellationToken);
        }

        var objects = KnowledgeObjectBuilder.Build(
            document,
            new KnowledgeObjectBuildOptions
            {
                FileKey = fileKey,
                DataSourceId = dataSource.ItemId,
                Title = fileName,
                Language = options.Language,
                IndexerId = options.IndexerId,
                SourceItemId = options.SourceItemId,
                ContentHash = contentHash,
                Structure = structure,
                ChartKeywords = options.ChartKeywords,
            },
            chunksByArticle);

        // What the document says about itself is worth one model call: a citation that names the
        // publication and issue is usable, and one that names the file it arrived in is not.
        var publication = await _publicationMetadataExtractor.ExtractAsync(document, cancellationToken);

        if (publication is not null)
        {
            objects[0].Put(publication);
        }

        // Replacing rather than merging keeps the store honest about what the file currently contains: a
        // figure removed from a revised file must not survive as a searchable row.
        var previous = await ReplacePreviousAsync(dataSource.ItemId, rootId, cancellationToken);

        foreach (var entry in objects)
        {
            await _store.CreateAsync(entry, cancellationToken);
        }

        _producedByDocument[GetProducedKey(dataSource.ItemId, rootId)] = objects;

        // An excluded object is stored but never indexed, so it can never be returned as an answer.
        var canonicalIds = objects
            .Where(entry => entry.Status != KnowledgeObjectStatus.Excluded)
            .Select(entry => entry.CanonicalId)
            .ToArray();

        await RemoveSupersededAsync(dataSource.ItemId, previous, objects, cancellationToken);

        if (canonicalIds.Length > 0)
        {
            await _indexingQueue.QueueSyncDataSourceDocumentsAsync(dataSource.ItemId, canonicalIds, cancellationToken);
        }

        return new KnowledgeIngestionResult
        {
            Success = true,
            RootId = rootId,
            ObjectCount = objects.Count,
            FigureCount = objects.Count(entry => entry.ObjectType is KnowledgeContentTypes.Figure or KnowledgeContentTypes.Chart),
            PendingDescriptionCount = objects.Count(entry => entry.Status == KnowledgeObjectStatus.PendingDescription),
        };
    }

    /// <inheritdoc />
    public async Task RemoveAsync(AIDataSource dataSource, string rootId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentException.ThrowIfNullOrEmpty(rootId);

        var objects = await ReplacePreviousAsync(dataSource.ItemId, rootId, cancellationToken);

        if (objects.Count == 0)
        {
            return;
        }

        var canonicalIds = objects.Select(entry => entry.CanonicalId).ToArray();

        foreach (var entry in objects)
        {
            if (string.IsNullOrWhiteSpace(entry.StoragePath))
            {
                continue;
            }

            try
            {
                await _fileStore.DeleteFileAsync(entry.StoragePath);
            }
            catch (Exception ex)
            {
                // An orphaned file costs disk space. Failing the removal because of one would leave the
                // knowledge behind instead, which costs correctness.
                _logger.LogWarning(ex, "Failed to delete figure file '{StoragePath}'.", entry.StoragePath);
            }
        }

        await _indexingQueue.QueueRemoveDataSourceDocumentsAsync(dataSource.ItemId, canonicalIds, cancellationToken);
    }

    /// <summary>
    /// Clears out whatever the document held before this call and returns it, including what an earlier call
    /// on this instance produced.
    /// </summary>
    /// <param name="dataSourceId">The owning data source.</param>
    /// <param name="rootId">The document's canonical identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The objects the document held before this call.</returns>
    /// <remarks>
    /// The store shares the caller's unit of work, and reading an uncommitted unit of work does not show
    /// what was written to it earlier in the same unit of work. Two byte-identical files in one submit would
    /// otherwise each store a full set of objects under one canonical identifier, because the read-back that
    /// decides what to replace sees neither of them. What this instance produced is therefore remembered,
    /// and deleted object by object.
    /// </remarks>
    private async Task<IReadOnlyCollection<KnowledgeObject>> ReplacePreviousAsync(
        string dataSourceId,
        string rootId,
        CancellationToken cancellationToken)
    {
        var stored = await _store.GetByRootIdAsync(dataSourceId, rootId, cancellationToken);

        await _store.DeleteByRootIdAsync(dataSourceId, rootId, cancellationToken);

        var key = GetProducedKey(dataSourceId, rootId);

        if (!_producedByDocument.TryGetValue(key, out var produced))
        {
            return stored;
        }

        _producedByDocument.Remove(key);

        // A unit of work that flushed early hands the same objects back from the read, and the delete by
        // root has already taken those out: only what the read did not see is deleted here.
        var handled = stored.Select(entry => entry.ItemId).ToHashSet(StringComparer.Ordinal);
        var previous = new List<KnowledgeObject>(stored);

        foreach (var entry in produced)
        {
            if (!handled.Add(entry.ItemId))
            {
                continue;
            }

            await _store.DeleteAsync(entry, cancellationToken);

            previous.Add(entry);
        }

        return previous;
    }

    /// <summary>
    /// Builds the key one document's objects are remembered under. The document identifier comes from the
    /// bytes alone, so the data source has to be part of it.
    /// </summary>
    /// <param name="dataSourceId">The owning data source.</param>
    /// <param name="rootId">The document's canonical identifier.</param>
    /// <returns>The key.</returns>
    private static string GetProducedKey(string dataSourceId, string rootId)
    {
        return $"{dataSourceId}|{rootId}";
    }

    /// <summary>
    /// Takes out of the index, and off the disk, whatever the previous ingest of the same document produced
    /// that this one did not produce for the index.
    /// </summary>
    /// <param name="dataSourceId">The owning data source.</param>
    /// <param name="previous">The objects the previous ingest stored.</param>
    /// <param name="current">The objects this ingest stored.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>
    /// Identical bytes produce identical identifiers, but not necessarily the same set of them: a reader or
    /// a processor that improved between two ingests can split the text into fewer chunks, drop a figure it
    /// now scores as decoration, or reclassify a figure as a chart. Deleting the objects by root replaces the
    /// store; the index only ever hears about identifiers it is told about, so the ones that vanished have
    /// to be named.
    /// <para>
    /// An object that is still produced but is now excluded counts as vanished. It is deliberately kept out
    /// of the sync queue, so naming it here is the only thing that takes the rows an earlier ingest wrote
    /// back out: an article reclassified as an advertisement stays answerable otherwise.
    /// </para>
    /// </remarks>
    private async Task RemoveSupersededAsync(
        string dataSourceId,
        IReadOnlyCollection<KnowledgeObject> previous,
        IReadOnlyList<KnowledgeObject> current,
        CancellationToken cancellationToken)
    {
        if (previous.Count == 0)
        {
            return;
        }

        // Only what this ingest offers the index counts as current. An excluded object is stored and never
        // queued, so leaving its identifier here would leave its old rows in place.
        var currentIds = current
            .Where(entry => entry.Status != KnowledgeObjectStatus.Excluded)
            .Select(entry => entry.CanonicalId)
            .ToHashSet(StringComparer.Ordinal);

        // The bytes of an excluded figure are still stored, so its file is not superseded.
        var currentPaths = current
            .Select(entry => entry.StoragePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.Ordinal);

        var removedIds = previous
            .Select(entry => entry.CanonicalId)
            .Where(id => !string.IsNullOrWhiteSpace(id) && !currentIds.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var entry in previous)
        {
            if (string.IsNullOrWhiteSpace(entry.StoragePath) || currentPaths.Contains(entry.StoragePath))
            {
                continue;
            }

            try
            {
                await _fileStore.DeleteFileAsync(entry.StoragePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete the superseded figure file '{StoragePath}'.", entry.StoragePath);
            }
        }

        if (removedIds.Length > 0)
        {
            await _indexingQueue.QueueRemoveDataSourceDocumentsAsync(dataSourceId, removedIds, cancellationToken);
        }
    }

    /// <summary>
    /// Writes the bytes of every figure that survived scoring, and records where they went on the element so
    /// the builder can carry the path onto the object.
    /// </summary>
    /// <param name="document">The ingested document.</param>
    /// <param name="dataSourceId">The owning data source.</param>
    /// <param name="fileName">The file name, used only for logging.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private async Task StoreFiguresAsync(
        IngestionDocument document,
        string dataSourceId,
        string fileName,
        CancellationToken cancellationToken)
    {
        foreach (var element in document.EnumerateContent())
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A skipped figure is not stored, and a repeat of artwork placed elsewhere in the document is the
            // same bytes under the same hash: the first placement stores them once.
            if (element is not IngestionDocumentImage image ||
                image.Content is not { } content ||
                image.GetMetadataString(FigureMetadataKeys.Tier) == FigureTiers.Skip ||
                image.GetMetadataString(FigureMetadataKeys.DuplicateOf) != null)
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
                var path = DocumentFileStoragePath.Create(FigureStorageSegment, dataSourceId, storedName, "figures");

                await using var stream = new MemoryStream(content.ToArray());
                await _fileStore.SaveFileAsync(path.StoragePath, stream);

                image.Metadata[FigureMetadataKeys.StoragePath] = path.StoragePath;
            }
            catch (Exception ex)
            {
                // The figure keeps its caption and loses only its picture. That is a worse figure, not a
                // failed document.
                _logger.LogWarning(ex, "Failed to store figure '{FigureId}' from '{FileName}'.", figureId, fileName);
            }
        }
    }

    /// <summary>
    /// Collects one article's text, which is everything in it that is not a figure, a caption or page
    /// furniture.
    /// </summary>
    /// <param name="document">The ingested document.</param>
    /// <param name="articleOrdinal">The article to collect.</param>
    /// <returns>The article text.</returns>
    private static string BuildArticleText(IngestionDocument document, int articleOrdinal)
    {
        var parts = new List<string>();

        foreach (var element in document.EnumerateContent())
        {
            if (element is IngestionDocumentImage or IngestionDocumentTable)
            {
                continue;
            }

            if (GetArticleOrdinal(element) != articleOrdinal)
            {
                continue;
            }

            if (element.GetMetadataString(ElementMetadataKeys.IsCaptionFor) != null)
            {
                continue;
            }

            if (element.IsDecoration())
            {
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
    /// Reads the article an element belongs to. An element nothing stamped belongs to the first article.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The article ordinal.</returns>
    private static int GetArticleOrdinal(IngestionDocumentElement element)
    {
        if (element.HasMetadata && element.Metadata.TryGetValue(ElementMetadataKeys.ArticleOrdinal, out var value))
        {
            return value switch
            {
                int ordinal => ordinal,
                long ordinal => (int)ordinal,
                _ => 1,
            };
        }

        return 1;
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream content, CancellationToken cancellationToken)
    {
        if (content is MemoryStream memory)
        {
            return memory.ToArray();
        }

        if (content.CanSeek)
        {
            content.Position = 0;
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        return buffer.ToArray();
    }
}
