using System.Threading.Channels;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Services;

internal sealed class AIDataSourceIndexingQueue : IAIDataSourceIndexingQueue
{
    private readonly ILogger<AIDataSourceIndexingQueue> _logger;
    private readonly Channel<AIDataSourceIndexingWorkItem> _channel = Channel.CreateUnbounded<AIDataSourceIndexingWorkItem>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    /// <summary>
    /// Initializes a new instance of the <see cref="AIDataSourceIndexingQueue"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public AIDataSourceIndexingQueue(ILogger<AIDataSourceIndexingQueue> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Queues sync data source.
    /// </summary>
    /// <param name="dataSource">The data source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask QueueSyncDataSourceAsync(AIDataSource dataSource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        var workItem = AIDataSourceIndexingWorkItem.ForSyncDataSource(dataSource.Clone());

        LogQueuedWorkItem(workItem, dataSource.ItemId, 0);

return _channel.Writer.WriteAsync(workItem, cancellationToken);
    }

    /// <summary>
    /// Queues delete data source.
    /// </summary>
    /// <param name="dataSource">The data source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask QueueDeleteDataSourceAsync(AIDataSource dataSource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        var workItem = AIDataSourceIndexingWorkItem.ForDeleteDataSource(dataSource.Clone());

        LogQueuedWorkItem(workItem, dataSource.ItemId, 0);

return _channel.Writer.WriteAsync(workItem, cancellationToken);
    }

    /// <summary>
    /// Queues sync source documents.
    /// </summary>
    /// <param name="sourceIndexProfileName">The source index profile name.</param>
    /// <param name="documentIds">The document ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask QueueSyncSourceDocumentsAsync(string sourceIndexProfileName, IReadOnlyCollection<string> documentIds, CancellationToken cancellationToken = default)
    {
        return QueueDocumentIdsAsync(sourceIndexProfileName, documentIds, AIDataSourceIndexingWorkItem.ForSyncSourceDocuments, cancellationToken);
    }

    /// <summary>
    /// Queues remove source documents.
    /// </summary>
    /// <param name="sourceIndexProfileName">The source index profile name.</param>
    /// <param name="documentIds">The document ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask QueueRemoveSourceDocumentsAsync(string sourceIndexProfileName, IReadOnlyCollection<string> documentIds, CancellationToken cancellationToken = default)
    {
        return QueueDocumentIdsAsync(sourceIndexProfileName, documentIds, AIDataSourceIndexingWorkItem.ForRemoveSourceDocuments, cancellationToken);
    }

    internal IAsyncEnumerable<AIDataSourceIndexingWorkItem> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }

    private ValueTask QueueDocumentIdsAsync(string sourceIndexProfileName, IReadOnlyCollection<string> documentIds, Func<string, IReadOnlyCollection<string>, AIDataSourceIndexingWorkItem> factory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceIndexProfileName);
        ArgumentNullException.ThrowIfNull(documentIds);

        var ids = documentIds.Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (ids.Length == 0)
        {
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("Skipped queueing data-source work item because no document ids remained after normalization.");
            }

            return ValueTask.CompletedTask;
        }

        var workItem = factory(sourceIndexProfileName, ids);

        LogQueuedWorkItem(workItem, sourceIndexProfileName, ids.Length);

return _channel.Writer.WriteAsync(workItem, cancellationToken);
    }

    private void LogQueuedWorkItem(AIDataSourceIndexingWorkItem workItem, string target, int documentCount)
    {
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace("Queued data-source work item {WorkItemType}. Target={Target}, DocumentCount={DocumentCount}.", workItem.Type, target, documentCount);
        }
    }
}

internal sealed class AIDataSourceIndexingWorkItem
{
    public AIDataSource DataSource { get; private init; }

    public IReadOnlyCollection<string> DocumentIds { get; private init; } = [];

    public string SourceIndexProfileName { get; private init; }

    public AIDataSourceIndexingWorkItemType Type { get; private init; }

    /// <summary>
    /// For sync data sources sync data source.
    /// </summary>
    /// <param name="dataSource">The data source.</param>
    public static AIDataSourceIndexingWorkItem ForSyncDataSource(AIDataSource dataSource)
    {
        return new()
        {
            DataSource = dataSource,
            Type = AIDataSourceIndexingWorkItemType.SyncDataSource,
        };
    }

    /// <summary>
    /// Fors delete data source.
    /// </summary>
    /// <param name="dataSource">The data source.</param>
    public static AIDataSourceIndexingWorkItem ForDeleteDataSource(AIDataSource dataSource)
    {
        return new()
        {
            DataSource = dataSource,
            Type = AIDataSourceIndexingWorkItemType.DeleteDataSource,
        };
    }

    /// <summary>
    /// Fors sync source documents.
    /// </summary>
    /// <param name="sourceIndexProfileName">The source index profile name.</param>
    /// <param name="documentIds">The document ids.</param>
    public static AIDataSourceIndexingWorkItem ForSyncSourceDocuments(string sourceIndexProfileName, IReadOnlyCollection<string> documentIds)
    {
        return new()
        {
            DocumentIds = documentIds,
            SourceIndexProfileName = sourceIndexProfileName,
            Type = AIDataSourceIndexingWorkItemType.SyncSourceDocuments,
        };
    }

    /// <summary>
    /// Fors remove source documents.
    /// </summary>
    /// <param name="sourceIndexProfileName">The source index profile name.</param>
    /// <param name="documentIds">The document ids.</param>
    public static AIDataSourceIndexingWorkItem ForRemoveSourceDocuments(string sourceIndexProfileName, IReadOnlyCollection<string> documentIds)
    {
        return new()
        {
            DocumentIds = documentIds,
            SourceIndexProfileName = sourceIndexProfileName,
            Type = AIDataSourceIndexingWorkItemType.RemoveSourceDocuments,
        };
    }
}

internal enum AIDataSourceIndexingWorkItemType
{
    SyncDataSource,
    DeleteDataSource,
    SyncSourceDocuments,
    RemoveSourceDocuments,
}
