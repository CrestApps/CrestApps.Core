using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;

namespace CrestApps.Core.Tests.Support;

/// <summary>
/// Records what was queued for indexing instead of queueing it.
/// </summary>
internal sealed class RecordingIndexingQueue : IAIDataSourceIndexingQueue
{
    /// <summary>
    /// Gets the document identifiers queued for synchronization.
    /// </summary>
    public List<string> Synced { get; } = [];

    /// <summary>
    /// Gets the document identifiers queued for removal.
    /// </summary>
    public List<string> Removed { get; } = [];

    /// <summary>
    /// Gets the data sources queued for a full synchronization.
    /// </summary>
    public List<string> SyncedDataSources { get; } = [];

    public ValueTask QueueSyncDataSourceAsync(AIDataSource dataSource, CancellationToken cancellationToken = default)
    {
        SyncedDataSources.Add(dataSource?.ItemId);

        return ValueTask.CompletedTask;
    }

    public ValueTask QueueDeleteDataSourceAsync(AIDataSource dataSource, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask QueueSyncSourceDocumentsAsync(string sourceIndexProfileName, IReadOnlyCollection<string> documentIds, CancellationToken cancellationToken = default)
    {
        Synced.AddRange(documentIds);

        return ValueTask.CompletedTask;
    }

    public ValueTask QueueRemoveSourceDocumentsAsync(string sourceIndexProfileName, IReadOnlyCollection<string> documentIds, CancellationToken cancellationToken = default)
    {
        Removed.AddRange(documentIds);

        return ValueTask.CompletedTask;
    }

    public ValueTask QueueSyncDataSourceDocumentsAsync(string dataSourceId, IReadOnlyCollection<string> documentIds, CancellationToken cancellationToken = default)
    {
        Synced.AddRange(documentIds);

        return ValueTask.CompletedTask;
    }

    public ValueTask QueueRemoveDataSourceDocumentsAsync(string dataSourceId, IReadOnlyCollection<string> documentIds, CancellationToken cancellationToken = default)
    {
        Removed.AddRange(documentIds);

        return ValueTask.CompletedTask;
    }
}
