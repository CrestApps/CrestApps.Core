using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.DataSources;
using CrestApps.Core.Infrastructure.Indexing.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Represents the default AI Data Source Indexing Service.
/// </summary>
public sealed class DefaultAIDataSourceIndexingService : IAIDataSourceIndexingService
{
    private const int BatchSize = 250;
    private const int MaxChunkIdsPerDocument = 1000;

    private readonly IAIDataSourceStore _dataSourceCatalog;
    private readonly ISearchIndexProfileManager _indexProfileManager;
    private readonly IAIDeploymentManager _deploymentManager;
    private readonly IAIClientFactory _aiClientFactory;
    private readonly IAITextNormalizer _textNormalizer;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DefaultAIDataSourceIndexingService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAIDataSourceIndexingService"/> class.
    /// </summary>
    /// <param name="dataSourceCatalog">The data source catalog.</param>
    /// <param name="indexProfileManager">The index profile manager.</param>
    /// <param name="deploymentManager">The deployment manager.</param>
    /// <param name="aiClientFactory">The ai client factory.</param>
    /// <param name="textNormalizer">The text normalizer.</param>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public DefaultAIDataSourceIndexingService(
        IAIDataSourceStore dataSourceCatalog,
        ISearchIndexProfileManager indexProfileManager,
        IAIDeploymentManager deploymentManager,
        IAIClientFactory aiClientFactory,
        IAITextNormalizer textNormalizer,
        IServiceProvider serviceProvider,
        TimeProvider timeProvider,
        ILogger<DefaultAIDataSourceIndexingService> logger)
    {
        _dataSourceCatalog = dataSourceCatalog;
        _indexProfileManager = indexProfileManager;
        _deploymentManager = deploymentManager;
        _aiClientFactory = aiClientFactory;
        _textNormalizer = textNormalizer;
        _serviceProvider = serviceProvider;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Syncs all.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task SyncAllAsync(CancellationToken cancellationToken = default)
    {
        var dataSources = await _dataSourceCatalog.GetAllAsync(cancellationToken);
        foreach (var dataSource in dataSources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await SyncDataSourceAsync(dataSource, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to synchronize data source '{DataSourceId}'.", dataSource.ItemId);
            }
        }
    }

    /// <summary>
    /// Syncs data source.
    /// </summary>
    /// <param name="dataSource">The data source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task SyncDataSourceAsync(AIDataSource dataSource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        var context = await TryCreateContextAsync(dataSource, requireSourceReader: true, cancellationToken);
        if (context == null)
        {
            return;
        }

        await EnsureKnowledgeBaseIndexAsync(context, cancellationToken);
        await context.ContentManager.DeleteByDataSourceIdAsync(context.KnowledgeBaseProfile, dataSource.ItemId, cancellationToken);
        var sourceDocuments = context.SourceHandler.ReadAsync(dataSource, cancellationToken);
        await IndexDocumentsAsync(context, sourceDocuments, deleteExistingChunks: false, cancellationToken);
    }

    /// <summary>
    /// Syncs source documents.
    /// </summary>
    /// <param name="documentIds">The document ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task SyncSourceDocumentsAsync(IEnumerable<string> documentIds, CancellationToken cancellationToken = default)
    {
        await SyncSourceDocumentsAsync(null, documentIds, cancellationToken);
    }

    /// <summary>
    /// Syncs source documents.
    /// </summary>
    /// <param name="sourceIndexProfileName">The source index profile name.</param>
    /// <param name="documentIds">The document ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task SyncSourceDocumentsAsync(string sourceIndexProfileName, IEnumerable<string> documentIds, CancellationToken cancellationToken = default)
    {
        var ids = NormalizeDocumentIds(documentIds);
        if (ids.Length == 0)
        {
            return;
        }

        foreach (var dataSource in await GetMatchingDataSourcesAsync(sourceIndexProfileName))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = await TryCreateContextAsync(dataSource, requireSourceReader: true, cancellationToken);
            if (context == null)
            {
                continue;
            }

            await EnsureKnowledgeBaseIndexAsync(context, cancellationToken);
            var sourceDocuments = context.SourceHandler.ReadByIdsAsync(dataSource, ids, cancellationToken);
            await IndexDocumentsAsync(context, sourceDocuments, deleteExistingChunks: true, cancellationToken);
        }
    }

    /// <summary>
    /// Syncs data source documents.
    /// </summary>
    /// <param name="dataSourceId">The data source id.</param>
    /// <param name="documentIds">The document ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task SyncDataSourceDocumentsAsync(string dataSourceId, IEnumerable<string> documentIds, CancellationToken cancellationToken = default)
    {
        var ids = NormalizeDocumentIds(documentIds);
        if (ids.Length == 0)
        {
            return;
        }

        var dataSource = await _dataSourceCatalog.FindByIdAsync(dataSourceId, cancellationToken);
        if (dataSource == null)
        {
            _logger.LogWarning("Skipping incremental data-source synchronization because data source '{DataSourceId}' was not found.", dataSourceId);

            return;
        }

        var context = await TryCreateContextAsync(dataSource, requireSourceReader: true, cancellationToken);
        if (context == null)
        {
            return;
        }

        await EnsureKnowledgeBaseIndexAsync(context, cancellationToken);
        var sourceDocuments = context.SourceHandler.ReadByIdsAsync(dataSource, ids, cancellationToken);
        await IndexDocumentsAsync(context, sourceDocuments, deleteExistingChunks: true, cancellationToken);
    }

    /// <summary>
    /// Removes source documents.
    /// </summary>
    /// <param name="documentIds">The document ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task RemoveSourceDocumentsAsync(IEnumerable<string> documentIds, CancellationToken cancellationToken = default)
    {
        await RemoveSourceDocumentsAsync(null, documentIds, cancellationToken);
    }

    /// <summary>
    /// Removes source documents.
    /// </summary>
    /// <param name="sourceIndexProfileName">The source index profile name.</param>
    /// <param name="documentIds">The document ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task RemoveSourceDocumentsAsync(string sourceIndexProfileName, IEnumerable<string> documentIds, CancellationToken cancellationToken = default)
    {
        var ids = NormalizeDocumentIds(documentIds);
        if (ids.Length == 0)
        {
            return;
        }

        foreach (var dataSource in await GetMatchingDataSourcesAsync(sourceIndexProfileName))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = await TryCreateContextAsync(dataSource, requireSourceReader: false, cancellationToken);
            if (context == null)
            {
                continue;
            }

            await DeleteReferencesAsync(context, ids, cancellationToken);
        }
    }

    /// <summary>
    /// Removes data source documents.
    /// </summary>
    /// <param name="dataSourceId">The data source id.</param>
    /// <param name="documentIds">The document ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task RemoveDataSourceDocumentsAsync(string dataSourceId, IEnumerable<string> documentIds, CancellationToken cancellationToken = default)
    {
        var ids = NormalizeDocumentIds(documentIds);
        if (ids.Length == 0)
        {
            return;
        }

        var dataSource = await _dataSourceCatalog.FindByIdAsync(dataSourceId, cancellationToken);
        if (dataSource == null)
        {
            _logger.LogWarning("Skipping incremental data-source deletion because data source '{DataSourceId}' was not found.", dataSourceId);

            return;
        }

        var context = await TryCreateContextAsync(dataSource, requireSourceReader: false, cancellationToken);
        if (context == null)
        {
            return;
        }

        await DeleteReferencesAsync(context, ids, cancellationToken);
    }

    /// <summary>
    /// Deletes data source documents.
    /// </summary>
    /// <param name="dataSource">The data source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task DeleteDataSourceDocumentsAsync(AIDataSource dataSource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        var context = await TryCreateContextAsync(dataSource, requireSourceReader: false, cancellationToken);
        if (context == null)
        {
            return;
        }

        await context.ContentManager.DeleteByDataSourceIdAsync(context.KnowledgeBaseProfile, dataSource.ItemId, cancellationToken);
    }

    private async Task IndexDocumentsAsync(DataSourceIndexingContext context, IAsyncEnumerable<KeyValuePair<string, SourceDocument>> sourceDocuments, bool deleteExistingChunks, CancellationToken cancellationToken)
    {
        var timestamp = _timeProvider.GetUtcNow().UtcDateTime;
        var indexedChunkCount = 0;
        var documents = new List<IndexDocument>();
        await foreach (var pair in sourceDocuments.WithCancellation(cancellationToken))
        {
            var referenceId = pair.Key;
            var sourceDocument = pair.Value;
            if (string.IsNullOrWhiteSpace(referenceId) || string.IsNullOrWhiteSpace(sourceDocument?.Content))
            {
                continue;
            }

            var normalizedTitle = _textNormalizer.NormalizeTitle(sourceDocument.Title);
            List<string> chunkTexts;

            if (sourceDocument.IsPreChunked)
            {
                // The row was built to sit inside one chunk and already carries its own title. Splitting it
                // would separate a figure description from the figure it describes.
                var normalized = await _textNormalizer.NormalizeContentAsync(sourceDocument.Content, cancellationToken);

                chunkTexts = string.IsNullOrWhiteSpace(normalized) ? [] : [normalized];
            }
            else
            {
                chunkTexts = await _textNormalizer.NormalizeAndChunkAsync(sourceDocument.Content, cancellationToken);
            }

            if (chunkTexts.Count == 0)
            {
                continue;
            }

            if (!sourceDocument.IsPreChunked && !string.IsNullOrWhiteSpace(normalizedTitle))
            {
                chunkTexts[0] = normalizedTitle + "\n" + chunkTexts[0];
            }

            var embeddings = await context.EmbeddingGenerator.GenerateAsync(chunkTexts, cancellationToken: cancellationToken);
            if (embeddings == null || embeddings.Count != chunkTexts.Count)
            {
                _logger.LogWarning("Skipping document '{ReferenceId}' for data source '{DataSourceId}' because embeddings could not be generated.", referenceId, context.DataSource.ItemId);
                continue;
            }

            if (deleteExistingChunks)
            {
                await DeleteReferencesAsync(context, [referenceId], cancellationToken);
            }

            var typed = ExtractTypedFields(sourceDocument.Fields, context.SourceHandler.ProducesTypedKnowledge);
            var filters = BuildFilterFields(sourceDocument.Fields, typed.Keys);
            for (var i = 0; i < chunkTexts.Count; i++)
            {
                var chunkId = $"{referenceId}_{i}";
                var fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    [DataSourceConstants.ColumnNames.ChunkId] = chunkId,
                    [DataSourceConstants.ColumnNames.ReferenceId] = referenceId,
                    [DataSourceConstants.ColumnNames.DataSourceId] = context.DataSource.ItemId,
                    [DataSourceConstants.ColumnNames.ReferenceType] = context.ReferenceType,
                    [DataSourceConstants.ColumnNames.ChunkIndex] = i,
                    [DataSourceConstants.ColumnNames.Title] = normalizedTitle,
                    [DataSourceConstants.ColumnNames.Content] = chunkTexts[i],
                    [DataSourceConstants.ColumnNames.Embedding] = embeddings[i].Vector.ToArray(),
                    [DataSourceConstants.ColumnNames.Timestamp] = timestamp,
                    [DataSourceConstants.ColumnNames.ContentType] = typed.ContentType,
                };

                foreach (var entry in typed.Columns)
                {
                    fields[entry.Key] = entry.Value;
                }
                if (filters != null)
                {
                    fields[DataSourceConstants.ColumnNames.Filters] = filters;
                }

                documents.Add(new IndexDocument { Id = chunkId, Fields = fields, });
                indexedChunkCount++;
                if (documents.Count >= BatchSize)
                {
                    await FlushAsync(context, documents, cancellationToken);
                }
            }
        }

        await FlushAsync(context, documents, cancellationToken);
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Synchronized {ChunkCount} knowledge-base chunk(s) for data source '{DataSourceId}'.", indexedChunkCount, context.DataSource.ItemId);
        }
    }

    private async Task FlushAsync(DataSourceIndexingContext context, List<IndexDocument> documents, CancellationToken cancellationToken)
    {
        if (documents.Count == 0)
        {
            return;
        }

        var success = await context.DocumentManager.AddOrUpdateAsync(context.KnowledgeBaseProfile, documents.ToArray(), cancellationToken);
        if (!success)
        {
            // The knowledge-base write failed. Surface it as an exception instead of swallowing it: a caller
            // that records progress after indexing (e.g. the Web data-source handler stamping crawl state
            // with a successful-index timestamp) must not treat a failed write as success, or the affected
            // pages would be marked indexed and never retried even though nothing was stored.
            _logger.LogWarning("Knowledge-base indexing reported a failure for data source '{DataSourceId}' in index '{IndexName}'.", context.DataSource.ItemId, context.KnowledgeBaseProfile.IndexFullName);

            throw new InvalidOperationException(
                $"Knowledge-base indexing failed for data source '{context.DataSource.ItemId}' in index '{context.KnowledgeBaseProfile.IndexFullName}'.");
        }

        documents.Clear();
    }

    private async Task EnsureKnowledgeBaseIndexAsync(DataSourceIndexingContext context, CancellationToken cancellationToken)
    {
        context.KnowledgeBaseProfile.IndexFullName ??= context.IndexManager.ComposeIndexFullName(context.KnowledgeBaseProfile);
        if (await context.IndexManager.ExistsAsync(context.KnowledgeBaseProfile, cancellationToken))
        {
            await TryUpgradeIndexAsync(context, cancellationToken);

            return;
        }

        var fields = await _indexProfileManager.GetFieldsAsync(context.KnowledgeBaseProfile, cancellationToken);
        if (fields == null || fields.Count == 0)
        {
            throw new InvalidOperationException($"The knowledge-base index profile '{context.KnowledgeBaseProfile.Name}' does not expose a schema.");
        }

        await context.IndexManager.CreateAsync(context.KnowledgeBaseProfile, fields, cancellationToken);
    }

    private async Task<DataSourceIndexingContext> TryCreateContextAsync(AIDataSource dataSource, bool requireSourceReader, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dataSource.AIKnowledgeBaseIndexProfileName))
        {
            return null;
        }

        var knowledgeBaseProfile = await _indexProfileManager.FindByNameAsync(dataSource.AIKnowledgeBaseIndexProfileName, cancellationToken);
        if (knowledgeBaseProfile == null)
        {
            _logger.LogWarning("Skipping data source '{DataSourceId}' because knowledge-base index profile '{IndexProfileName}' was not found.", dataSource.ItemId, dataSource.AIKnowledgeBaseIndexProfileName);

            return null;
        }

        if (!string.Equals(knowledgeBaseProfile.Type, IndexProfileTypes.DataSource, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Skipping data source '{DataSourceId}' because knowledge-base index profile '{IndexProfileName}' is not a data-source profile.", dataSource.ItemId, knowledgeBaseProfile.Name);

            return null;
        }

        var indexManager = _serviceProvider.GetKeyedService<ISearchIndexManager>(knowledgeBaseProfile.ProviderName);
        var documentManager = _serviceProvider.GetKeyedService<ISearchDocumentManager>(knowledgeBaseProfile.ProviderName);
        var contentManager = _serviceProvider.GetKeyedService<IDataSourceContentManager>(knowledgeBaseProfile.ProviderName);
        if (indexManager == null || documentManager == null || contentManager == null)
        {
            _logger.LogWarning("Skipping data source '{DataSourceId}' because provider '{ProviderName}' is not fully configured for data-source indexing.", dataSource.ItemId, knowledgeBaseProfile.ProviderName);

            return null;
        }

        if (!requireSourceReader)
        {
            return new DataSourceIndexingContext(dataSource, knowledgeBaseProfile, indexManager, documentManager, contentManager, null, null, null);
        }

        var deploymentName = knowledgeBaseProfile.EmbeddingDeploymentName;

        if (knowledgeBaseProfile.TryGet(out DataSourceIndexProfileMetadata profileMetadata) &&
            !string.IsNullOrEmpty(profileMetadata.EmbeddingDeploymentName))
        {
            deploymentName = profileMetadata.EmbeddingDeploymentName;
        }

        if (string.IsNullOrWhiteSpace(deploymentName))
        {
            _logger.LogWarning("Skipping data source '{DataSourceId}' because knowledge-base index '{IndexProfileName}' has no embedding deployment configured.", dataSource.ItemId, knowledgeBaseProfile.Name);

            return null;
        }

        var deployment = await _deploymentManager.FindByNameAsync(deploymentName, cancellationToken);

        if (deployment == null)
        {
            _logger.LogWarning("Skipping data source '{DataSourceId}' because knowledge-base index '{IndexProfileName}' has no embedding deployment configured.", dataSource.ItemId, knowledgeBaseProfile.Name);

            return null;
        }

        var embeddingGenerator = await _aiClientFactory.CreateEmbeddingGeneratorAsync(deployment);

        if (embeddingGenerator == null)
        {
            _logger.LogWarning("Skipping data source '{DataSourceId}' because knowledge-base index '{IndexProfileName}' has no embedding deployment configured.", dataSource.ItemId, knowledgeBaseProfile.Name);

            return null;
        }

        var sourceType = AIDataSourceSourceHelper.GetSource(dataSource);
        var sourceHandler = _serviceProvider.GetKeyedService<IAIDataSourceSourceHandler>(sourceType);
        if (sourceHandler == null)
        {
            _logger.LogWarning("Skipping data source '{DataSourceId}' because source type '{SourceType}' is not registered.", dataSource.ItemId, sourceType);

            return null;
        }

        var referenceType = await sourceHandler.GetReferenceTypeAsync(dataSource, cancellationToken);
        if (string.IsNullOrWhiteSpace(referenceType))
        {
            _logger.LogWarning("Skipping data source '{DataSourceId}' because source type '{SourceType}' could not resolve a reference type.", dataSource.ItemId, sourceType);

            return null;
        }

        return new DataSourceIndexingContext(dataSource, knowledgeBaseProfile, indexManager, documentManager, contentManager, sourceHandler, referenceType, embeddingGenerator);
    }

    private static string[] NormalizeDocumentIds(IEnumerable<string> documentIds)
    {
        return documentIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
    }

    private async Task<IReadOnlyCollection<AIDataSource>> GetMatchingDataSourcesAsync(string sourceIndexProfileName)
    {
        var dataSources = await _dataSourceCatalog.GetAllAsync();
        return dataSources
            .Where(dataSource => string.Equals(AIDataSourceSourceHelper.GetSource(dataSource), AIDataSourceSourceTypes.SearchIndexProfile, StringComparison.OrdinalIgnoreCase))
            .Where(dataSource => string.IsNullOrWhiteSpace(sourceIndexProfileName) ||
                string.Equals(dataSource.SourceIndexProfileName, sourceIndexProfileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    /// <summary>
    /// Brings an index that already exists up to the current schema.
    /// </summary>
    /// <param name="context">The indexing context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>
    /// A provider that cannot add fields keeps serving the index it has. The typed columns are then absent,
    /// every row reads as text, and retrieval still works - it just cannot filter by type until the index is
    /// recreated. That is a reduced capability, never a failure.
    /// </remarks>
    private async Task TryUpgradeIndexAsync(DataSourceIndexingContext context, CancellationToken cancellationToken)
    {
        var fields = await _indexProfileManager.GetFieldsAsync(context.KnowledgeBaseProfile, cancellationToken);

        if (fields == null || fields.Count == 0)
        {
            return;
        }

        try
        {
            if (await context.IndexManager.TryAddFieldsAsync(context.KnowledgeBaseProfile, fields, cancellationToken))
            {
                return;
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Provider '{ProviderName}' cannot add fields to index '{IndexName}'. Typed filters are unavailable on it until it is recreated.",
                    context.KnowledgeBaseProfile.ProviderName,
                    context.KnowledgeBaseProfile.IndexFullName);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to upgrade the schema of index '{IndexName}'. Indexing continues against the existing schema.", context.KnowledgeBaseProfile.IndexFullName);
        }
    }

    /// <summary>
    /// Deletes the rows of the supplied references, preferring a provider that can delete by predicate.
    /// </summary>
    /// <param name="context">The indexing context.</param>
    /// <param name="referenceIds">The reference identifiers to delete.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>
    /// The fallback asks for a fixed span of chunk identifiers per reference, which is a thousand
    /// identifiers to delete a handful of rows. A provider that can express "where referenceId in (...)"
    /// does it in one statement.
    /// </remarks>
    private static async Task DeleteReferencesAsync(
        DataSourceIndexingContext context,
        string[] referenceIds,
        CancellationToken cancellationToken)
    {
        if (referenceIds.Length == 0)
        {
            return;
        }

        if (await context.ContentManager.DeleteByReferenceIdsAsync(context.KnowledgeBaseProfile, context.DataSource.ItemId, referenceIds, cancellationToken))
        {
            return;
        }

        await context.DocumentManager.DeleteAsync(context.KnowledgeBaseProfile, BuildChunkIds(referenceIds), cancellationToken);
    }

    private static List<string> BuildChunkIds(IEnumerable<string> referenceIds, int maxChunksPerDocument = MaxChunkIdsPerDocument)
    {
        var chunkIds = new List<string>();

        foreach (var referenceId in referenceIds)
        {
            for (var i = 0; i < maxChunksPerDocument; i++)
            {
                chunkIds.Add($"{referenceId}_{i}");
            }
        }

        return chunkIds;
    }

    /// <summary>
    /// Copies the caller-supplied fields into the filter bag, leaving out the ones promoted to real
    /// columns so a value is never stored twice.
    /// </summary>
    /// <param name="sourceFields">The fields the source handler supplied.</param>
    /// <param name="promoted">The field names written to their own columns.</param>
    /// <returns>The filter bag, or <see langword="null"/> when nothing is left.</returns>
    private static Dictionary<string, object> BuildFilterFields(Dictionary<string, object> sourceFields, IReadOnlyCollection<string> promoted)
    {
        if (sourceFields == null || sourceFields.Count == 0)
        {
            return null;
        }

        var filters = new Dictionary<string, object>(sourceFields, StringComparer.OrdinalIgnoreCase);

        foreach (var name in promoted)
        {
            filters.Remove(name);
        }

        return filters.Count == 0 ? null : filters;
    }

    /// <summary>
    /// Pulls the typed discriminators out of the caller-supplied fields.
    /// </summary>
    /// <param name="sourceFields">The fields the source handler supplied.</param>
    /// <returns>The content type, the columns to write, and which field names were consumed.</returns>
    /// <remarks>
    /// A source that says nothing about its type is text, which is also what every row written before these
    /// columns existed is read as.
    /// </remarks>
    private static TypedFields ExtractTypedFields(Dictionary<string, object> sourceFields, bool producesTypedKnowledge)
    {
        var columns = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var consumed = new List<string>
        {
            DataSourceConstants.ColumnNames.ContentType,
        };

        var contentType = KnowledgeContentTypes.Text;

        // Only a handler that produces typed knowledge means the knowledge base's own discriminators by
        // these names. Anything else consumes nothing, so a source whose documents have always carried a
        // top-level "contentType" of their own keeps it in the per-row bag and still filters on their field
        // rather than having it silently written into the column and dropped from the bag. The row's own
        // content type stays the default, which is what every row written before these columns existed
        // already reads as.
        if (!producesTypedKnowledge)
        {
            return new TypedFields(contentType, columns, []);
        }

        if (sourceFields != null)
        {
            if (sourceFields.TryGetValue(DataSourceConstants.ColumnNames.ContentType, out var rawContentType) &&
                rawContentType is string text &&
                !string.IsNullOrWhiteSpace(text))
            {
                contentType = text;
            }

            foreach (var name in new[] { DataSourceConstants.ColumnNames.RootId, DataSourceConstants.ColumnNames.ParentId, DataSourceConstants.ColumnNames.Page })
            {
                consumed.Add(name);

                if (sourceFields.TryGetValue(name, out var value) && value != null)
                {
                    columns[name] = value;
                }
            }
        }

        return new TypedFields(contentType, columns, consumed);
    }

    private sealed record TypedFields(string ContentType, Dictionary<string, object> Columns, IReadOnlyCollection<string> Keys);

    private sealed record DataSourceIndexingContext(
        AIDataSource DataSource,
        SearchIndexProfile KnowledgeBaseProfile,
        ISearchIndexManager IndexManager,
        ISearchDocumentManager DocumentManager,
        IDataSourceContentManager ContentManager,
        IAIDataSourceSourceHandler SourceHandler,
        string ReferenceType,
        IEmbeddingGenerator<string, Embedding<float>> EmbeddingGenerator);
}
