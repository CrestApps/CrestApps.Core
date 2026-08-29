using CrestApps.Core.AI.DataSources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.WebCrawlers;

/// <summary>
/// Periodically re-crawls every enabled <see cref="Models.WebCrawler"/> and re-indexes only the pages
/// that changed since the last run. It is a thin scheduler around <see cref="IWebCrawlerReindexPlanner"/>,
/// mirroring the framework's data-source alignment background service so the reusable diffing logic can be
/// driven by any host that prefers its own scheduling.
/// </summary>
internal sealed class WebCrawlerReindexBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeProvider _timeProvider;
    private readonly WebCrawlerOptions _options;
    private readonly ILogger<WebCrawlerReindexBackgroundService> _logger;
    private readonly Dictionary<string, DateTimeOffset> _lastRunUtc = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="WebCrawlerReindexBackgroundService"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="options">The web-crawler options.</param>
    /// <param name="logger">The logger.</param>
    public WebCrawlerReindexBackgroundService(
        IServiceProvider serviceProvider,
        TimeProvider timeProvider,
        IOptions<WebCrawlerOptions> options,
        ILogger<WebCrawlerReindexBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.ReindexCheckIntervalMinutes));
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
                await ReindexDueCrawlersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while re-indexing web crawlers.");
            }
        }
    }

    private async Task ReindexDueCrawlersAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var crawlerStore = scope.ServiceProvider.GetService<IWebCrawlerStore>();

        if (crawlerStore is null)
        {
            return;
        }

        var crawlers = (await crawlerStore.GetAllAsync(cancellationToken))
            .Where(crawler => crawler.Enabled)
            .ToArray();

        if (crawlers.Length == 0)
        {
            return;
        }

        var planner = scope.ServiceProvider.GetRequiredService<IWebCrawlerReindexPlanner>();
        var crawlStateStore = scope.ServiceProvider.GetRequiredService<IWebCrawlStateStore>();
        var now = _timeProvider.GetUtcNow();

        foreach (var crawler in crawlers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var reindexInterval = ResolveReindexInterval(crawler.ReindexIntervalMinutes);
            var lastRun = await GetLastRunAsync(crawler.ItemId, crawlStateStore, cancellationToken);

            if (now - lastRun < reindexInterval)
            {
                continue;
            }

            try
            {
                await planner.PlanAndEnqueueAsync(crawler, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to re-index web crawler '{CrawlerId}'.", crawler.ItemId);
            }

            _lastRunUtc[crawler.ItemId] = now;
        }
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

    private async Task<DateTimeOffset> GetLastRunAsync(string crawlerId, IWebCrawlStateStore crawlStateStore, CancellationToken cancellationToken)
    {
        if (_lastRunUtc.TryGetValue(crawlerId, out var cached))
        {
            return cached;
        }

        // Seed the last-run time from the persisted crawl state so a restart does not force an immediate
        // re-crawl of every configured site.
        var states = await crawlStateStore.GetAsync(crawlerId, cancellationToken);
        var lastSeen = states.Count == 0
            ? DateTimeOffset.MinValue
            : new DateTimeOffset(states.Max(state => state.LastSeenUtc), TimeSpan.Zero);

        _lastRunUtc[crawlerId] = lastSeen;

        return lastSeen;
    }
}
