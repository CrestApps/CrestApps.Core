using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.WebCrawlers;

/// <summary>
/// The default <see cref="IWebCrawlerReindexService"/>. It loads every enabled crawler and, for
/// <see cref="ReindexDueAsync"/>, re-indexes only those due per their re-index interval — where "due" is
/// derived from the persisted crawl state (the most recent time a page was seen), so the decision survives
/// restarts and does not depend on the caller keeping any state.
/// </summary>
public sealed class WebCrawlerReindexService : IWebCrawlerReindexService
{
    private readonly IWebCrawlerStore _crawlerStore;
    private readonly IWebCrawlStateStore _crawlStateStore;
    private readonly IWebCrawlerReindexPlanner _planner;
    private readonly IAIDataSourceStore _dataSourceStore;
    private readonly WebCrawlerOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WebCrawlerReindexService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebCrawlerReindexService"/> class that re-indexes every
    /// enabled crawler regardless of the kind of data source it feeds.
    /// </summary>
    /// <param name="crawlerStore">The crawler store.</param>
    /// <param name="crawlStateStore">The crawl-state store.</param>
    /// <param name="planner">The re-index planner.</param>
    /// <param name="options">The web-crawler options.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public WebCrawlerReindexService(
        IWebCrawlerStore crawlerStore,
        IWebCrawlStateStore crawlStateStore,
        IWebCrawlerReindexPlanner planner,
        IOptions<WebCrawlerOptions> options,
        TimeProvider timeProvider,
        ILogger<WebCrawlerReindexService> logger)
        : this(crawlerStore, crawlStateStore, planner, null, options, timeProvider, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebCrawlerReindexService"/> class.
    /// </summary>
    /// <param name="crawlerStore">The crawler store.</param>
    /// <param name="crawlStateStore">The crawl-state store.</param>
    /// <param name="planner">The re-index planner.</param>
    /// <param name="dataSourceStore">
    /// The data source store, used to leave crawlers that feed an <c>Ingested</c> data source to the indexer
    /// run service. <see langword="null"/> re-indexes every enabled crawler.
    /// </param>
    /// <param name="options">The web-crawler options.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public WebCrawlerReindexService(
        IWebCrawlerStore crawlerStore,
        IWebCrawlStateStore crawlStateStore,
        IWebCrawlerReindexPlanner planner,
        IAIDataSourceStore dataSourceStore,
        IOptions<WebCrawlerOptions> options,
        TimeProvider timeProvider,
        ILogger<WebCrawlerReindexService> logger)
    {
        _crawlerStore = crawlerStore;
        _crawlStateStore = crawlStateStore;
        _planner = planner;
        _dataSourceStore = dataSourceStore;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task ReindexDueAsync(CancellationToken cancellationToken = default)
        => ReindexAsync(dueOnly: true, cancellationToken);

    /// <inheritdoc />
    public Task ReindexAllAsync(CancellationToken cancellationToken = default)
        => ReindexAsync(dueOnly: false, cancellationToken);

    private async Task ReindexAsync(bool dueOnly, CancellationToken cancellationToken)
    {
        var crawlers = (await _crawlerStore.GetAllAsync(cancellationToken))
            .Where(crawler => crawler.Enabled)
            .ToArray();

        if (crawlers.Length == 0)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();

        foreach (var crawler in crawlers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A crawler pointed at an Ingested data source is an indexer: its pages become typed knowledge
            // objects through the indexer run service, which keeps its own per-item state. Planning it here
            // as well would run it twice and overwrite that state with the planner's.
            if (await FeedsIngestedDataSourceAsync(crawler, cancellationToken))
            {
                continue;
            }

            if (dueOnly && !await IsDueAsync(crawler, now, cancellationToken))
            {
                continue;
            }

            try
            {
                await _planner.PlanAndEnqueueAsync(crawler, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to re-index web crawler '{CrawlerId}'.", crawler.ItemId);
            }
        }
    }

    private async Task<bool> FeedsIngestedDataSourceAsync(WebCrawler crawler, CancellationToken cancellationToken)
    {
        if (_dataSourceStore is null || string.IsNullOrWhiteSpace(crawler.AIDataSourceId))
        {
            return false;
        }

        try
        {
            var dataSource = await _dataSourceStore.FindByIdAsync(crawler.AIDataSourceId, cancellationToken);

            return dataSource is not null &&
                string.Equals(dataSource.Source, AIDataSourceSourceTypes.File, StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A data source that cannot be read is treated as a Web one, which is what every crawler fed
            // before Ingested data sources existed.
            _logger.LogWarning(ex, "Failed to read the data source of web crawler '{CrawlerId}'.", crawler.ItemId);

            return false;
        }
    }

    private async Task<bool> IsDueAsync(WebCrawler crawler, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var reindexInterval = ResolveReindexInterval(crawler.ReindexIntervalMinutes);
        var states = await _crawlStateStore.GetAsync(crawler.ItemId, cancellationToken);
        var lastRun = states.Count == 0
            ? DateTimeOffset.MinValue
            : new DateTimeOffset(states.Max(state => state.LastSeenUtc), TimeSpan.Zero);

        return now - lastRun >= reindexInterval;
    }

    private TimeSpan ResolveReindexInterval(int? crawlerInterval)
    {
        var minutes = crawlerInterval ?? _options.DefaultReindexIntervalMinutes;

        if (minutes <= 0)
        {
            minutes = _options.DefaultReindexIntervalMinutes;
        }

        return TimeSpan.FromMinutes(Math.Max(1, minutes));
    }
}
