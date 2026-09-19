using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Ingestion;
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
/// <remarks>
/// Every crawler is this feature's to run, whichever pipeline reads it. One pointed at a <c>Web</c> data
/// source goes to the re-index planner. One pointed at an ingested data source is read by the shared
/// ingestion run service, which turns its pages into typed knowledge objects and keeps its own per-item
/// state; that path is shared with file sources, but the record is still ours, so a host may enable this
/// feature without the file sources feature and have both kinds of crawler run.
/// </remarks>
public sealed class WebCrawlerReindexService : IWebCrawlerReindexService
{
    private readonly IWebCrawlerStore _crawlerStore;
    private readonly IWebCrawlStateStore _crawlStateStore;
    private readonly IWebCrawlerReindexPlanner _planner;
    private readonly IAIDataSourceStore _dataSourceStore;
    private readonly IIngestionRunService _ingestionRunService;
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
        : this(crawlerStore, crawlStateStore, planner, null, options, timeProvider, logger, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebCrawlerReindexService"/> class.
    /// </summary>
    /// <param name="crawlerStore">The crawler store.</param>
    /// <param name="crawlStateStore">The crawl-state store.</param>
    /// <param name="planner">The re-index planner.</param>
    /// <param name="dataSourceStore">
    /// The data source store, used to tell a crawler that feeds an <c>Ingested</c> data source from one that
    /// feeds a <c>Web</c> data source. <see langword="null"/> plans every enabled crawler as a Web one.
    /// </param>
    /// <param name="options">The web-crawler options.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="ingestionRunService">
    /// The shared ingestion run service, which reads a crawler that feeds an ingested data source.
    /// <see langword="null"/> leaves those crawlers alone rather than planning them as Web ones, which would
    /// overwrite the per-item state the ingestion pipeline keeps.
    /// </param>
    public WebCrawlerReindexService(
        IWebCrawlerStore crawlerStore,
        IWebCrawlStateStore crawlStateStore,
        IWebCrawlerReindexPlanner planner,
        IAIDataSourceStore dataSourceStore,
        IOptions<WebCrawlerOptions> options,
        TimeProvider timeProvider,
        ILogger<WebCrawlerReindexService> logger,
        IIngestionRunService ingestionRunService = null)
    {
        _ingestionRunService = ingestionRunService;
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

            // A crawler pointed at an Ingested data source takes the ingestion path instead of the planner:
            // its pages become typed knowledge objects and its progress is kept as per-item state. Planning
            // it as a Web crawler as well would read it twice and overwrite that state.
            var ingested = await FeedsIngestedDataSourceAsync(crawler, cancellationToken);

            if (ingested && _ingestionRunService is null)
            {
                continue;
            }

            if (dueOnly && !(ingested
                ? IsIngestionDue(crawler, now)
                : await IsDueAsync(crawler, now, cancellationToken)))
            {
                continue;
            }

            try
            {
                if (ingested)
                {
                    await _ingestionRunService.RunAsync(crawler, cancellationToken);
                }
                else
                {
                    await _planner.PlanAndEnqueueAsync(crawler, cancellationToken);
                }
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

    /// <summary>
    /// Determines whether a crawler read by the ingestion pipeline is due again.
    /// </summary>
    /// <param name="crawler">The crawler.</param>
    /// <param name="now">The current time.</param>
    /// <returns><see langword="true"/> when its interval has elapsed since the last run started.</returns>
    /// <remarks>
    /// The ingestion path keeps no crawl state, so being due is read from the run summary the run service
    /// writes back, which is the same rule the file sources feature applies to its own records.
    /// </remarks>
    private bool IsIngestionDue(WebCrawler crawler, DateTimeOffset now)
    {
        if (!crawler.TryGet<FileSourceRunSummary>(out var last) || last.StartedUtc == default)
        {
            return true;
        }

        return now - new DateTimeOffset(last.StartedUtc, TimeSpan.Zero) >= ResolveReindexInterval(crawler.ReindexIntervalMinutes);
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
