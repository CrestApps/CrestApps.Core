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

    public AIDataSourceIndexingQueue(ILogger<AIDataSourceIndexingQueue> logger)
    {
        _logger = logger;
    }

    public ValueTask QueueSyncDataSourceAsync(AIDataSource dataSource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        var workItem = AIDataSourceIndexingWorkItem.ForSyncDataSource(dataSource.Clone());

        LogQueuedWorkItem(workItem, dataSource.ItemId, 0);

        return _channel.Writer.WriteAsync(workItem, cancellationToken);
    }

    public ValueTask QueueDeleteDataSourceAsync(AIDataSource dataSource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        var workItem = AIDataSourceIndexingWorkItem.ForDeleteDataSource(dataSource.Clone());

        LogQueuedWorkItem(workItem, dataSource.ItemId, 0);

        return _channel.Writer.WriteAsync(workItem, cancellationToken);
    }

    public ValueTask QueueSyncSourceDocumentsAsync(string sourceIndexProfileName, IReadOnlyCollection<string> documentIds, CancellationToken cancellationToken = default)
    {
        return QueueDocumentIdsAsync(sourceIndexProfileName, documentIds, AIDataSourceIndexingWorkItem.ForSyncSourceDocuments, cancellationToken);
    }

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

    public static AIDataSourceIndexingWorkItem ForSyncDataSource(AIDataSource dataSource)
    {
        return new()
        {
            DataSource = dataSource,
            Type = AIDataSourceIndexingWorkItemType.SyncDataSource,
        };
    }

    public static AIDataSourceIndexingWorkItem ForDeleteDataSource(AIDataSource dataSource)
    {
        return new()
        {
            DataSource = dataSource,
            Type = AIDataSourceIndexingWorkItemType.DeleteDataSource,
        };
    }

    public static AIDataSourceIndexingWorkItem ForSyncSourceDocuments(string sourceIndexProfileName, IReadOnlyCollection<string> documentIds)
    {
        return new()
        {
            DocumentIds = documentIds,
            SourceIndexProfileName = sourceIndexProfileName,
            Type = AIDataSourceIndexingWorkItemType.SyncSourceDocuments,
        };
    }

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
