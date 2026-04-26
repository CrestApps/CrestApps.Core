using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Services;

internal sealed class AIDataSourceSearchDocumentHandler : ISearchDocumentHandler
{
    private readonly IAIDataSourceIndexingQueue _indexingQueue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AIDataSourceSearchDocumentHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIDataSourceSearchDocumentHandler"/> class.
    /// </summary>
    /// <param name="indexingQueue">The indexing queue.</param>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="logger">The logger.</param>
    public AIDataSourceSearchDocumentHandler(
        IAIDataSourceIndexingQueue indexingQueue,
        IServiceProvider serviceProvider,
        ILogger<AIDataSourceSearchDocumentHandler> logger)
    {
        _indexingQueue = indexingQueue;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Documentss added or updated.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="documentIds">The document ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task DocumentsAddedOrUpdatedAsync(IIndexProfileInfo profile, IReadOnlyCollection<string> documentIds, CancellationToken cancellationToken = default)
    {
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace("Search document event '{EventName}' received for index profile '{IndexProfileId}' with {DocumentCount} document id(s).", nameof(DocumentsAddedOrUpdatedAsync), profile?.IndexProfileId, documentIds?.Count ?? 0);
        }

        await QueueSourceDocumentsAsync(profile, documentIds, isDelete: false, cancellationToken);
    }

    /// <summary>
    /// Documentss deleted.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="documentIds">The document ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task DocumentsDeletedAsync(IIndexProfileInfo profile, IReadOnlyCollection<string> documentIds, CancellationToken cancellationToken = default)
    {
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace("Search document event '{EventName}' received for index profile '{IndexProfileId}' with {DocumentCount} document id(s).", nameof(DocumentsDeletedAsync), profile?.IndexProfileId, documentIds?.Count ?? 0);
        }

        await QueueSourceDocumentsAsync(profile, documentIds, isDelete: true, cancellationToken);
    }

    private async Task QueueSourceDocumentsAsync(IIndexProfileInfo profile, IReadOnlyCollection<string> documentIds, bool isDelete, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(documentIds);

        var ids = documentIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (ids.Length == 0 || string.IsNullOrWhiteSpace(profile.IndexProfileId))
        {
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("Skipped queueing data-source synchronization from {Handler} because no document ids or source index profile id were available.", nameof(AIDataSourceSearchDocumentHandler));
            }

            return;
        }

        var indexProfileManager = _serviceProvider.GetService<ISearchIndexProfileManager>();
        var dataSourceCatalog = _serviceProvider.GetService<ICatalog<AIDataSource>>();

        if (indexProfileManager == null || dataSourceCatalog == null)
        {
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("Skipped queueing data-source synchronization because required services were unavailable. HasIndexProfileManager={HasIndexProfileManager}, HasDataSourceCatalog={HasDataSourceCatalog}.", indexProfileManager != null, dataSourceCatalog != null);
            }

            return;
        }

        var sourceProfile = await indexProfileManager.FindByIdAsync(profile.IndexProfileId, cancellationToken);

        if (sourceProfile == null ||
            string.IsNullOrWhiteSpace(sourceProfile.Name) ||
            string.Equals(sourceProfile.Type, IndexProfileTypes.DataSource, StringComparison.OrdinalIgnoreCase))
        {
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("Skipped queueing data-source synchronization because source profile '{IndexProfileId}' was missing or not a supported source index.", profile.IndexProfileId);
            }

            return;
        }

        var dataSources = await dataSourceCatalog.GetAllAsync(cancellationToken);

        if (!dataSources.Any(dataSource =>
                string.Equals(dataSource.SourceIndexProfileName, sourceProfile.Name, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(dataSource.AIKnowledgeBaseIndexProfileName)))
        {
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("Skipped queueing data-source synchronization because source profile '{SourceIndexProfileName}' has no mapped AI data sources.", sourceProfile.Name);
            }

            return;
        }

        try
        {
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("Queueing {Operation} for source index profile '{SourceIndexProfileName}' with {DocumentCount} document id(s).", isDelete ? nameof(IAIDataSourceIndexingQueue.QueueRemoveSourceDocumentsAsync) : nameof(IAIDataSourceIndexingQueue.QueueSyncSourceDocumentsAsync), sourceProfile.Name, ids.Length);
            }

            if (isDelete)
            {
                await _indexingQueue.QueueRemoveSourceDocumentsAsync(sourceProfile.Name, ids, cancellationToken);
            }
            else
            {
                await _indexingQueue.QueueSyncSourceDocumentsAsync(sourceProfile.Name, ids, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue data-source synchronization for source index profile '{IndexProfileName}'.", sourceProfile.Name);
        }
    }
}
