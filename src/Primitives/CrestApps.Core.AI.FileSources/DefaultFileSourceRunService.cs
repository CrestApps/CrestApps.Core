using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Ingestion.Knowledge;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.FileSources;

/// <summary>
/// Runs one indexer against its connector and keeps its data source in step with the source.
/// </summary>
/// <remarks>
/// The whole subsystem turns on one rule: removals happen only when the connector said its listing was
/// complete. Everything else here is bookkeeping. A source that could not be fully listed and was treated as
/// though it had been deletes every item it failed to see, and nothing downstream can tell that apart from a
/// genuine deletion.
/// </remarks>
public sealed class DefaultFileSourceRunService : IFileSourceRunService
{
    private readonly IIngestionConnectorResolver _connectorResolver;
    private readonly IWebCrawlStateStore _stateStore;
    private readonly IWebCrawlerStore _indexerStore;
    private readonly IKnowledgeIngestionService _ingestionService;
    private readonly IAIDataSourceStore _dataSourceStore;
    private readonly FileSourceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DefaultFileSourceRunService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultFileSourceRunService"/> class.
    /// </summary>
    /// <param name="connectorResolver">The connector resolver.</param>
    /// <param name="stateStore">The per-item state store.</param>
    /// <param name="indexerStore">The store the run summary is recorded on.</param>
    /// <param name="ingestionService">The knowledge ingestion service.</param>
    /// <param name="dataSourceStore">The data source store.</param>
    /// <param name="options">The indexer options.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public DefaultFileSourceRunService(
        IIngestionConnectorResolver connectorResolver,
        IWebCrawlStateStore stateStore,
        IWebCrawlerStore indexerStore,
        IKnowledgeIngestionService ingestionService,
        IAIDataSourceStore dataSourceStore,
        IOptions<FileSourceOptions> options,
        TimeProvider timeProvider,
        ILogger<DefaultFileSourceRunService> logger)
    {
        _connectorResolver = connectorResolver;
        _stateStore = stateStore;
        _indexerStore = indexerStore;
        _ingestionService = ingestionService;
        _dataSourceStore = dataSourceStore;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IndexerRunSummary> RunAsync(WebCrawler indexer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(indexer);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var previous = indexer.TryGet<IndexerRunSummary>(out var last) ? last : null;
        var summary = new IndexerRunSummary
        {
            StartedUtc = now,
            Status = FileSourceRunStatus.Running,
        };

        if (!indexer.Enabled || string.IsNullOrWhiteSpace(indexer.AIDataSourceId))
        {
            return await FailAsync(indexer, summary, "The indexer is disabled or has no data source.", cancellationToken);
        }

        var connector = _connectorResolver.Get(indexer.Source);

        if (connector is null)
        {
            return await FailAsync(indexer, summary, $"No connector named '{indexer.Source}' is registered.", cancellationToken);
        }

        var dataSource = await _dataSourceStore.FindByIdAsync(indexer.AIDataSourceId, cancellationToken);

        if (dataSource is null)
        {
            return await FailAsync(indexer, summary, "The indexer's data source no longer exists.", cancellationToken);
        }

        // The objects a run produces are read back only by the Ingested source handler. Filling any other
        // kind of data source would store knowledge nothing ever indexes.
        if (!string.Equals(dataSource.Source, AIDataSourceSourceTypes.File, StringComparison.OrdinalIgnoreCase))
        {
            return await FailAsync(indexer, summary, "The indexer's data source is not an Ingested data source.", cancellationToken);
        }

        // The record says a run is in progress before the work starts, so a long run is visible rather than
        // looking like an indexer that has not run since yesterday.
        await SaveSummaryAsync(indexer, summary, cancellationToken);

        IngestionDiscoveryResult discovery;

        try
        {
            discovery = await connector.DiscoverAsync(indexer, previous?.DiscoveryCursor, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Discovery failed for indexer '{IndexerId}'.", indexer.ItemId);

            return await FailAsync(indexer, summary, "Discovery failed. Nothing was ingested and nothing was removed.", cancellationToken);
        }

        summary.ItemsDiscovered = discovery.Items.Count;
        summary.DiscoveryCompleted = discovery.IsComplete;
        summary.DiscoveryCursor = discovery.DiscoveryCursor;
        summary.Error = discovery.Message;

        var existing = await _stateStore.GetAsync(indexer.ItemId, cancellationToken);
        var states = existing.ToDictionary(state => state.Url, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var candidates = new List<Candidate>();
        var budget = ResolveBudget(indexer);

        foreach (var item in discovery.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            seen.Add(item.ItemId);
            states.TryGetValue(item.ItemId, out var state);

            if (state is not null && !HasChanged(state, item))
            {
                await TouchAsync(state, now, cancellationToken);
                summary.ItemsSkipped++;

                continue;
            }

            if (candidates.Count >= budget)
            {
                // What is left is picked up next run. The listing said what exists, so what was not reached
                // is still known to exist and is never removed.
                continue;
            }

            candidates.Add(new Candidate(item, state));
        }

        await ProcessAsync(connector, indexer, dataSource, candidates, states, summary, now, cancellationToken);

        if (discovery.IsComplete)
        {
            summary.ItemsDeleted = await RemoveMissingAsync(indexer, dataSource, states, seen, cancellationToken);
        }
        else if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Indexer '{IndexerId}' listed its source only partially, so nothing was removed. {Message}",
                indexer.ItemId,
                discovery.Message);
        }

        summary.CompletedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        summary.Status = summary.ItemsFailed > 0 || !summary.DiscoveryCompleted
            ? FileSourceRunStatus.PartiallyCompleted
            : FileSourceRunStatus.Succeeded;

        await SaveSummaryAsync(indexer, summary, cancellationToken);

        return summary;
    }

    /// <summary>
    /// Fetches the selected items, several at a time, and ingests them one at a time.
    /// </summary>
    /// <param name="connector">The connector.</param>
    /// <param name="indexer">The configured indexer.</param>
    /// <param name="dataSource">The target data source.</param>
    /// <param name="candidates">The items that need reading.</param>
    /// <param name="states">Everything recorded for this indexer, keyed by item.</param>
    /// <param name="summary">The run summary being filled in.</param>
    /// <param name="now">The run timestamp.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>
    /// Fetching waits on a network and is worth overlapping; ingestion writes to the knowledge store and the
    /// state store, neither of which is safe to use from two threads at once. So a bounded window of fetches
    /// runs ahead of a single-threaded ingest, which is where the time actually goes without putting two
    /// writers on one session.
    /// </remarks>
    private async Task ProcessAsync(
        IIngestionConnector connector,
        WebCrawler indexer,
        AIDataSource dataSource,
        List<Candidate> candidates,
        Dictionary<string, WebCrawlState> states,
        IndexerRunSummary summary,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return;
        }

        var window = Math.Max(1, _options.MaxConcurrentFetches);
        var inFlight = new Queue<(Candidate Candidate, Task<IngestionItemContent> Fetch)>();
        var index = 0;

        try
        {
            while (index < candidates.Count || inFlight.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                while (index < candidates.Count && inFlight.Count < window)
                {
                    var candidate = candidates[index++];

                    inFlight.Enqueue((candidate, FetchAsync(connector, indexer, candidate.Item.ItemId, cancellationToken)));
                }

                var (next, fetch) = inFlight.Dequeue();

                if (await IngestAsync(indexer, dataSource, next, fetch, states, summary, now, cancellationToken))
                {
                    summary.ItemsIndexed++;
                }
                else
                {
                    summary.ItemsFailed++;
                }
            }
        }
        finally
        {
            // A run that stops part-way still owns the fetches it started. Each is released once it settles,
            // so a cancelled run does not leak an open connection per in-flight item.
            while (inFlight.Count > 0)
            {
                var (_, fetch) = inFlight.Dequeue();

                await DiscardAsync(fetch);
            }
        }
    }

    private static async Task DiscardAsync(Task<IngestionItemContent> fetch)
    {
        try
        {
            var content = await fetch;

            if (content is not null)
            {
                await content.DisposeAsync();
            }
        }
        catch (Exception)
        {
            // The fetch already failed or was cancelled; there is nothing left to release.
        }
    }

    /// <summary>
    /// Fetches one item, turning a failure into no content rather than a faulted task.
    /// </summary>
    /// <param name="connector">The connector.</param>
    /// <param name="indexer">The configured indexer.</param>
    /// <param name="itemId">The item to fetch.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The content, or <see langword="null"/>.</returns>
    private async Task<IngestionItemContent> FetchAsync(
        IIngestionConnector connector,
        WebCrawler indexer,
        string itemId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await connector.FetchAsync(indexer, itemId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch '{ItemId}' for indexer '{IndexerId}'.", itemId, indexer.ItemId);

            return null;
        }
    }

    /// <summary>
    /// Turns one fetched item into knowledge.
    /// </summary>
    /// <param name="indexer">The configured indexer.</param>
    /// <param name="dataSource">The target data source.</param>
    /// <param name="candidate">The item and what was recorded about it last run.</param>
    /// <param name="fetch">The fetch already under way.</param>
    /// <param name="states">Everything recorded for this indexer, keyed by item.</param>
    /// <param name="summary">The run summary being filled in.</param>
    /// <param name="now">The run timestamp.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the item was ingested.</returns>
    private async Task<bool> IngestAsync(
        WebCrawler indexer,
        AIDataSource dataSource,
        Candidate candidate,
        Task<IngestionItemContent> fetch,
        Dictionary<string, WebCrawlState> states,
        IndexerRunSummary summary,
        DateTime now,
        CancellationToken cancellationToken)
    {
        IngestionItemContent content = null;

        try
        {
            content = await fetch;

            if (content?.Content is null)
            {
                return false;
            }

            var result = await _ingestionService.IngestAsync(
                dataSource,
                content.Content,
                string.IsNullOrWhiteSpace(content.FileName) ? candidate.Item.ItemId : content.FileName,
                content.MediaType,
                BuildIngestionOptions(indexer, candidate.Item),
                cancellationToken);

            if (!result.Success)
            {
                return false;
            }

            summary.FiguresDiscovered += result.FigureCount;
            summary.FiguresPendingDescription += result.PendingDescriptionCount;

            var previousRootId = candidate.State?.ContentHash;

            await SaveStateAsync(indexer, candidate.Item, candidate.State, result.RootId, now, states, cancellationToken);
            await RemoveSupersededDocumentAsync(dataSource, previousRootId, result.RootId, states, cancellationToken);

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // One item that cannot be read is one item. The run continues, because stopping would leave
            // every later item unindexed because of a single bad file.
            _logger.LogWarning(ex, "Failed to ingest '{ItemId}' for indexer '{IndexerId}'.", candidate.Item.ItemId, indexer.ItemId);

            return false;
        }
        finally
        {
            if (content is not null)
            {
                await content.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Removes the document an item produced last time, once its content has changed enough to produce a
    /// different one.
    /// </summary>
    /// <param name="dataSource">The target data source.</param>
    /// <param name="previousRootId">The document the item produced last run, or <see langword="null"/>.</param>
    /// <param name="rootId">The document the item produced this run.</param>
    /// <param name="states">Everything recorded for this indexer, keyed by item.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>
    /// A document's identifier comes from its bytes, so a revised file is a new document, and the old one
    /// would otherwise stay searchable beside it forever. The old document is kept when another item of
    /// this indexer still produces it, because the same file placed twice is one document by design.
    /// </remarks>
    private async Task RemoveSupersededDocumentAsync(
        AIDataSource dataSource,
        string previousRootId,
        string rootId,
        Dictionary<string, WebCrawlState> states,
        CancellationToken cancellationToken)
    {
        if (!IsDocumentRootId(previousRootId) || string.Equals(previousRootId, rootId, StringComparison.Ordinal))
        {
            return;
        }

        if (states.Values.Any(state => string.Equals(state.ContentHash, previousRootId, StringComparison.Ordinal)))
        {
            return;
        }

        try
        {
            await _ingestionService.RemoveAsync(dataSource, previousRootId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The new document is stored and queued. A stale one that could not be removed is a duplicate
            // answer, not a lost one, and the next run of this item tries again.
            _logger.LogWarning(ex, "Failed to remove the superseded document '{RootId}' from data source '{DataSourceId}'.", previousRootId, dataSource.ItemId);
        }
    }

    /// <summary>
    /// Determines whether a stored value names an ingested document rather than a legacy page hash.
    /// </summary>
    /// <param name="value">The stored value.</param>
    /// <returns><see langword="true"/> when the value is a document identifier.</returns>
    private static bool IsDocumentRootId(string value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            value.StartsWith(KnowledgeObjectTypes.Document + ':', StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds the per-run ingestion options from what the indexer was configured with.
    /// </summary>
    /// <param name="indexer">The configured indexer.</param>
    /// <param name="item">The item being ingested.</param>
    /// <returns>The options.</returns>
    private static KnowledgeIngestionOptions BuildIngestionOptions(WebCrawler indexer, IngestionItemRef item)
    {
        var metadata = indexer.GetOrCreate<IndexerMetadata>();

        return new KnowledgeIngestionOptions
        {
            IndexerId = indexer.ItemId,
            SourceItemId = item.ItemId,
            FigureMode = metadata.FigureMode,
            VisionDeploymentName = metadata.VisionDeploymentName,
            MaxFigureDescriptionsPerDocument = metadata.MaxFigureDescriptionsPerDocument,
            Language = metadata.Language,
        };
    }

    /// <summary>
    /// Resolves how many items this run may read, preferring the indexer's own ceiling.
    /// </summary>
    /// <param name="indexer">The configured indexer.</param>
    /// <returns>The budget.</returns>
    private int ResolveBudget(WebCrawler indexer)
    {
        var configured = indexer.GetOrCreate<IndexerMetadata>().MaxItemsPerRun;

        return Math.Max(1, configured is > 0 ? configured.Value : _options.MaxItemsPerRun);
    }

    /// <summary>
    /// Removes the items the source no longer holds.
    /// </summary>
    /// <param name="indexer">The configured indexer.</param>
    /// <param name="dataSource">The target data source.</param>
    /// <param name="states">Everything recorded for this indexer.</param>
    /// <param name="seen">Everything the source reported this run.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many items were removed.</returns>
    private async Task<int> RemoveMissingAsync(
        WebCrawler indexer,
        AIDataSource dataSource,
        Dictionary<string, WebCrawlState> states,
        HashSet<string> seen,
        CancellationToken cancellationToken)
    {
        var missing = states.Values.Where(state => !seen.Contains(state.Url)).ToList();

        if (missing.Count == 0)
        {
            return 0;
        }

        var remaining = states.Values
            .Where(state => seen.Contains(state.Url))
            .Select(state => state.ContentHash)
            .Where(IsDocumentRootId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var state in missing)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The same file placed twice is one document. It goes when the last item that produces it goes.
            if (IsDocumentRootId(state.ContentHash) && !remaining.Contains(state.ContentHash))
            {
                await _ingestionService.RemoveAsync(dataSource, state.ContentHash, cancellationToken);
            }
        }

        await _stateStore.DeleteByUrlsAsync(indexer.ItemId, missing.Select(state => state.Url), cancellationToken);

        return missing.Count;
    }

    /// <summary>
    /// Decides whether an item needs re-reading.
    /// </summary>
    /// <param name="state">What was recorded last run.</param>
    /// <param name="item">What the source reports now.</param>
    /// <returns><see langword="true"/> when the item has changed or nothing says it has not.</returns>
    /// <remarks>
    /// The token is compared verbatim and never parsed: it is an ETag for one source and a modified time and
    /// size for another, and what it means is the connector's business. An item with no token is re-read,
    /// because "no evidence of change" is not evidence of no change.
    /// </remarks>
    private static bool HasChanged(WebCrawlState state, IngestionItemRef item)
    {
        if (string.IsNullOrEmpty(item.ChangeToken))
        {
            return true;
        }

        return !string.Equals(state.ChangeFrequency, item.ChangeToken, StringComparison.Ordinal);
    }

    private async Task SaveStateAsync(
        WebCrawler indexer,
        IngestionItemRef item,
        WebCrawlState state,
        string rootId,
        DateTime now,
        Dictionary<string, WebCrawlState> states,
        CancellationToken cancellationToken)
    {
        if (state is null)
        {
            state = new WebCrawlState
            {
                ItemId = UniqueId.GenerateId(),
                Source = indexer.ItemId,
                Url = item.ItemId,
            };

            Apply(state, item, rootId, now);

            await _stateStore.CreateAsync(state, cancellationToken);

            // The run decides what to delete from this same dictionary. A file that was moved keeps its
            // bytes, so the new path produces the document the old path produced; leaving the new state out
            // of the snapshot makes the old path look like the last item that produced it, and the run then
            // deletes the document it has just finished ingesting.
            states[state.Url] = state;

            return;
        }

        Apply(state, item, rootId, now);

        await _stateStore.UpdateAsync(state, cancellationToken);
    }

    private static void Apply(WebCrawlState state, IngestionItemRef item, string rootId, DateTime now)
    {
        // ChangeFrequency carries the connector's opaque token, and ContentHash the identifier of the
        // document the item produced, which is what a removal has to name.
        state.ChangeFrequency = item.ChangeToken;
        state.LastModifiedUtc = item.LastModifiedUtc?.UtcDateTime;
        state.ContentHash = rootId;
        state.LastIndexedUtc = now;
        state.LastSeenUtc = now;
    }

    private async Task TouchAsync(WebCrawlState state, DateTime now, CancellationToken cancellationToken)
    {
        state.LastSeenUtc = now;

        await _stateStore.UpdateAsync(state, cancellationToken);
    }

    /// <summary>
    /// Ends a run that could not do its work, recording why.
    /// </summary>
    /// <param name="indexer">The configured indexer.</param>
    /// <param name="summary">The run summary.</param>
    /// <param name="error">What stopped the run.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The summary.</returns>
    private async Task<IndexerRunSummary> FailAsync(
        WebCrawler indexer,
        IndexerRunSummary summary,
        string error,
        CancellationToken cancellationToken)
    {
        summary.CompletedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        summary.Status = FileSourceRunStatus.Failed;
        summary.Error = error;

        await SaveSummaryAsync(indexer, summary, cancellationToken);

        return summary;
    }

    /// <summary>
    /// Records the summary on the indexer, so the last run is visible without reading a log.
    /// </summary>
    /// <param name="indexer">The configured indexer.</param>
    /// <param name="summary">The run summary.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private async Task SaveSummaryAsync(WebCrawler indexer, IndexerRunSummary summary, CancellationToken cancellationToken)
    {
        try
        {
            indexer.Put(summary);

            await _indexerStore.UpdateAsync(indexer, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Failing to record what a run did must not fail the run: the knowledge is already stored.
            _logger.LogWarning(ex, "Failed to record the run summary for indexer '{IndexerId}'.", indexer.ItemId);
        }
    }

    private readonly record struct Candidate(IngestionItemRef Item, WebCrawlState State);
}
