using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.FileSources;

/// <summary>
/// A thin hosted job that periodically runs the indexers that are due.
/// </summary>
/// <remarks>
/// All of the run logic lives in <see cref="IFileSourceRunService"/>, so a host that prefers its own scheduling
/// — or an operator pressing a button — drives the same code this does. Whether a record is due is read from
/// the run summary stored on it, so the schedule survives a restart.
/// </remarks>
internal sealed class FileSourceBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly FileSourceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FileSourceBackgroundService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSourceBackgroundService"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="options">The indexer options.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public FileSourceBackgroundService(
        IServiceProvider serviceProvider,
        IOptions<FileSourceOptions> options,
        TimeProvider timeProvider,
        ILogger<FileSourceBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.RunCheckIntervalMinutes));
        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await RunDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while running indexers.");
            }
        }
    }

    private async Task RunDueAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();

        var services = scope.ServiceProvider;
        var runService = services.GetService<IFileSourceRunService>();
        var store = services.GetService<IWebCrawlerStore>();
        var dataSourceStore = services.GetService<IAIDataSourceStore>();
        var connectorResolver = services.GetService<IIngestionConnectorResolver>();

        if (runService is null || store is null || dataSourceStore is null || connectorResolver is null)
        {
            return;
        }

        var committer = services.GetService<IStoreCommitter>();
        var now = _timeProvider.GetUtcNow();

        foreach (var indexer in await store.GetAllAsync(stoppingToken))
        {
            stoppingToken.ThrowIfCancellationRequested();

            if (!indexer.Enabled || string.IsNullOrWhiteSpace(indexer.AIDataSourceId))
            {
                continue;
            }

            if (connectorResolver.Get(indexer.Source) is null)
            {
                continue;
            }

            // Only a record that feeds an Ingested data source is an indexer. A crawler that feeds a Web
            // data source is driven by the web-crawler re-index service, and running it here as well would
            // run it twice and overwrite its crawl state with this service's.
            if (!await FeedsIngestedDataSourceAsync(dataSourceStore, indexer, stoppingToken))
            {
                continue;
            }

            if (!IsDue(indexer, now))
            {
                continue;
            }

            try
            {
                var summary = await runService.RunAsync(indexer, stoppingToken);

                // The run wrote knowledge objects, per-item state and its own summary through the
                // transactional store. A request commits those through its filter; this scope has no
                // filter, so the session is flushed here or everything the run did is lost.
                if (committer is not null)
                {
                    await committer.CommitAsync(stoppingToken);
                }

                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(
                        "Indexer '{IndexerId}' saw {Discovered} item(s), ingested {Ingested}, removed {Removed}, failed {Failed}. Listing complete: {Complete}.",
                        indexer.ItemId,
                        summary.ItemsDiscovered,
                        summary.ItemsIndexed,
                        summary.ItemsDeleted,
                        summary.ItemsFailed,
                        summary.DiscoveryCompleted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One indexer that cannot run is one indexer. The others still get their turn.
                _logger.LogError(ex, "Indexer '{IndexerId}' failed to run.", indexer.ItemId);
            }
        }
    }

    /// <summary>
    /// Decides whether an indexer is due, from the run summary stored on it.
    /// </summary>
    /// <param name="indexer">The indexer.</param>
    /// <param name="now">The current time.</param>
    /// <returns><see langword="true"/> when the indexer has never run or its interval has elapsed.</returns>
    private bool IsDue(WebCrawler indexer, DateTimeOffset now)
    {
        var every = TimeSpan.FromMinutes(Math.Max(1, indexer.ReindexIntervalMinutes ?? _options.DefaultRunIntervalMinutes));

        if (!indexer.TryGet<IndexerRunSummary>(out var last) || last.StartedUtc == default)
        {
            return true;
        }

        return now - new DateTimeOffset(last.StartedUtc, TimeSpan.Zero) >= every;
    }

    private async Task<bool> FeedsIngestedDataSourceAsync(IAIDataSourceStore dataSourceStore, WebCrawler indexer, CancellationToken cancellationToken)
    {
        try
        {
            var dataSource = await dataSourceStore.FindByIdAsync(indexer.AIDataSourceId, cancellationToken);

            return dataSource is not null &&
                string.Equals(dataSource.Source, AIDataSourceSourceTypes.File, StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read the data source of indexer '{IndexerId}'.", indexer.ItemId);

            return false;
        }
    }
}
