using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.FileSources;

/// <summary>
/// The default <see cref="IFileSourceScheduler"/>.
/// </summary>
/// <remarks>
/// Every <see cref="FileSource"/> is a candidate, because reading files into a knowledge base is the only
/// thing a file source does. A <see cref="WebCrawler"/> is a candidate only when it feeds an ingested data
/// source: a crawler pointed at a <c>Web</c> data source is driven by the re-index service instead, and
/// running it here as well would run it twice and overwrite its crawl state with this service's.
/// <para>
/// The web crawler store is optional, so a host that enabled file sources without the web crawlers feature
/// can still construct this. Without it there are no crawlers to sweep, only file sources.
/// </para>
/// </remarks>
public sealed class DefaultFileSourceScheduler : IFileSourceScheduler
{
    private readonly IFileSourceStore _fileSourceStore;
    private readonly IAIDataSourceStore _dataSourceStore;
    private readonly IIngestionConnectorResolver _connectorResolver;
    private readonly IFileSourceRunService _runService;
    private readonly IStoreCommitter _committer;
    private readonly IWebCrawlerStore _webCrawlerStore;
    private readonly FileSourceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DefaultFileSourceScheduler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultFileSourceScheduler"/> class.
    /// </summary>
    /// <param name="fileSourceStore">The file source store.</param>
    /// <param name="dataSourceStore">The data source store.</param>
    /// <param name="connectorResolver">The connector resolver.</param>
    /// <param name="runService">The run service.</param>
    /// <param name="options">The file source options.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="committer">
    /// The store committer, when the host has one. A request commits through its own filter; a background
    /// pass has no filter, so what a run wrote is flushed here or it is lost.
    /// </param>
    /// <param name="webCrawlerStore">
    /// The web crawler store, when the host enabled that feature. Without it only file sources are swept.
    /// </param>
    public DefaultFileSourceScheduler(
        IFileSourceStore fileSourceStore,
        IAIDataSourceStore dataSourceStore,
        IIngestionConnectorResolver connectorResolver,
        IFileSourceRunService runService,
        IOptions<FileSourceOptions> options,
        TimeProvider timeProvider,
        ILogger<DefaultFileSourceScheduler> logger,
        IStoreCommitter committer = null,
        IWebCrawlerStore webCrawlerStore = null)
    {
        _fileSourceStore = fileSourceStore;
        _webCrawlerStore = webCrawlerStore;
        _dataSourceStore = dataSourceStore;
        _connectorResolver = connectorResolver;
        _runService = runService;
        _committer = committer;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<FileSourceSchedulerResult> RunDueAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var candidates = await GetCandidatesAsync(cancellationToken);
        var ran = 0;
        var failed = 0;

        foreach (var ingestionSource in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsDue(ingestionSource, now))
            {
                continue;
            }

            try
            {
                var summary = await _runService.RunAsync(ingestionSource, cancellationToken);

                ran++;

                if (_committer is not null)
                {
                    await _committer.CommitAsync(cancellationToken);
                }

                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(
                        "Source '{SourceId}' saw {Discovered} item(s), ingested {Ingested}, removed {Removed}, failed {Failed}. Listing complete: {Complete}.",
                        ingestionSource.ItemId,
                        summary.ItemsDiscovered,
                        summary.ItemsIndexed,
                        summary.ItemsDeleted,
                        summary.ItemsFailed,
                        summary.DiscoveryCompleted);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One source that cannot run is one source. The others still get their turn.
                failed++;
                _logger.LogError(ex, "Source '{SourceId}' failed to run.", ingestionSource.ItemId);
            }
        }

        return new FileSourceSchedulerResult(candidates.Count, ran, failed);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IngestionSource>> GetDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var candidates = await GetCandidatesAsync(cancellationToken);

        return candidates.Where(candidate => IsDue(candidate, now)).ToArray();
    }

    /// <inheritdoc />
    public bool IsDue(IngestionSource ingestionSource, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(ingestionSource);

        var every = TimeSpan.FromMinutes(Math.Max(1, ingestionSource.ReindexIntervalMinutes ?? _options.DefaultRunIntervalMinutes));

        if (!ingestionSource.TryGet<FileSourceRunSummary>(out var last) || last.StartedUtc == default)
        {
            return true;
        }

        return now - new DateTimeOffset(last.StartedUtc, TimeSpan.Zero) >= every;
    }

    /// <summary>
    /// Collects everything this scheduler is responsible for, whether or not it is due yet.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The sources that could be run.</returns>
    private async Task<IReadOnlyList<IngestionSource>> GetCandidatesAsync(CancellationToken cancellationToken)
    {
        var candidates = new List<IngestionSource>();

        foreach (var fileSource in await _fileSourceStore.GetAllAsync(cancellationToken))
        {
            if (IsRunnable(fileSource))
            {
                candidates.Add(fileSource);
            }
        }

        if (_webCrawlerStore is null)
        {
            return candidates;
        }

        foreach (var crawler in await _webCrawlerStore.GetAllAsync(cancellationToken))
        {
            if (!IsRunnable(crawler))
            {
                continue;
            }

            // A crawler reaches the ingestion pipeline only by pointing at an ingested data source. One
            // pointed at a Web data source belongs to the re-index service and is left alone here.
            if (await FeedsIngestedDataSourceAsync(crawler, cancellationToken))
            {
                candidates.Add(crawler);
            }
        }

        return candidates;
    }

    /// <summary>
    /// Determines whether a record could be run at all, before asking whether it is due.
    /// </summary>
    /// <param name="ingestionSource">The configured source.</param>
    /// <returns><see langword="true"/> when it is enabled, has a target, and has a connector to read it.</returns>
    private bool IsRunnable(IngestionSource ingestionSource)
    {
        if (!ingestionSource.Enabled || string.IsNullOrWhiteSpace(ingestionSource.AIDataSourceId))
        {
            return false;
        }

        // Whatever registered the connector may be gone. The record is still stored, but nothing can read it.
        return _connectorResolver.Get(ingestionSource.Source) is not null;
    }

    private async Task<bool> FeedsIngestedDataSourceAsync(IngestionSource ingestionSource, CancellationToken cancellationToken)
    {
        try
        {
            var dataSource = await _dataSourceStore.FindByIdAsync(ingestionSource.AIDataSourceId, cancellationToken);

            return dataSource is not null &&
                string.Equals(dataSource.Source, AIDataSourceSourceTypes.File, StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read the data source of '{SourceId}'.", ingestionSource.ItemId);

            return false;
        }
    }
}
