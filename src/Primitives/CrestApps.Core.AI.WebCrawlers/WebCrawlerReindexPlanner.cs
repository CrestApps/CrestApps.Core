using CrestApps.Core;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.WebCrawlers.Strategies;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.WebCrawlers;

/// <summary>
/// The default <see cref="IWebCrawlerReindexPlanner"/>. It re-runs a crawler's strategy discovery,
/// compares it against the stored crawl state, and enqueues only the pages that changed: new and modified
/// pages are queued for re-indexing into the crawler's target data source, pages missing from the crawl
/// are queued for deletion and their crawl state is removed. Change detection is generic (a page changed
/// when its advertised last-modified timestamp is newer than the recorded one), so it works for any
/// strategy; pages without a timestamp are refreshed by the nightly full data-source alignment instead.
/// </summary>
public sealed class WebCrawlerReindexPlanner : IWebCrawlerReindexPlanner
{
    private readonly IWebCrawlerStrategyResolver _strategyResolver;
    private readonly IWebCrawlStateStore _crawlStateStore;
    private readonly IAIDataSourceIndexingQueue _indexingQueue;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WebCrawlerReindexPlanner> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebCrawlerReindexPlanner"/> class.
    /// </summary>
    /// <param name="strategyResolver">The strategy resolver.</param>
    /// <param name="crawlStateStore">The crawl-state store.</param>
    /// <param name="indexingQueue">The data source indexing queue.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public WebCrawlerReindexPlanner(
        IWebCrawlerStrategyResolver strategyResolver,
        IWebCrawlStateStore crawlStateStore,
        IAIDataSourceIndexingQueue indexingQueue,
        TimeProvider timeProvider,
        ILogger<WebCrawlerReindexPlanner> logger)
    {
        _strategyResolver = strategyResolver;
        _crawlStateStore = crawlStateStore;
        _indexingQueue = indexingQueue;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<WebCrawlerReindexResult> PlanAndEnqueueAsync(WebCrawler crawler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(crawler);

        if (!crawler.Enabled || string.IsNullOrWhiteSpace(crawler.AIDataSourceId))
        {
            return WebCrawlerReindexResult.Empty;
        }

        var strategy = _strategyResolver.Get(crawler.Source);

        if (strategy is null)
        {
            return WebCrawlerReindexResult.Empty;
        }

        IReadOnlyList<CrawledPageRef> discoveredRefs;

        try
        {
            discoveredRefs = await strategy.DiscoverAsync(crawler, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Web-crawler discovery failed for crawler '{CrawlerId}' → data source '{DataSourceId}'. The site could not be crawled.",
                crawler.ItemId,
                crawler.AIDataSourceId);

            return WebCrawlerReindexResult.Failed(
                $"The site could not be crawled: {ex.Message} Verify the site is reachable and that it is not blocking the crawler's user agent.");
        }

        if (discoveredRefs.Count == 0)
        {
            // Discovery came back empty. Do NOT diff against the stored state here: treating every known page
            // as "removed" would wipe the knowledge base whenever a site transiently blocks the crawler or its
            // sitemap is briefly unreachable. Leave the existing state untouched and report the condition so the
            // user can check their configuration or unblock the crawler.
            _logger.LogWarning(
                "Web-crawler discovery for crawler '{CrawlerId}' → data source '{DataSourceId}' returned no pages; existing crawl state was left untouched.",
                crawler.ItemId,
                crawler.AIDataSourceId);

            return WebCrawlerReindexResult.NoPagesDiscovered(
                "No pages were discovered. The site may be blocking the crawler (ensure your server and robots.txt allow the crawler's user agent), or the configured base/sitemap URL may be wrong, unreachable, or empty.");
        }

        var discovered = discoveredRefs
            .GroupBy(pageRef => pageRef.Url, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var existingByUrl = (await _crawlStateStore.GetAsync(crawler.ItemId, cancellationToken))
            .GroupBy(state => state.Url, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var toIndex = new List<string>();
        var unchanged = 0;
        var newCount = 0;
        var changedCount = 0;

        foreach (var (url, pageRef) in discovered)
        {
            if (!existingByUrl.TryGetValue(url, out var state))
            {
                await _crawlStateStore.CreateAsync(
                    new WebCrawlState
                    {
                        ItemId = UniqueId.GenerateId(),
                        Source = crawler.ItemId,
                        Url = url,
                        LastModifiedUtc = pageRef.LastModifiedUtc?.UtcDateTime,
                        ChangeFrequency = pageRef.ChangeFrequency,
                        LastSeenUtc = now,
                        LastIndexedUtc = DateTime.MinValue,
                    },
                    cancellationToken);

                toIndex.Add(url);
                newCount++;

                continue;
            }

            state.LastSeenUtc = now;

            if (HasChanged(pageRef, state))
            {
                state.LastModifiedUtc = pageRef.LastModifiedUtc?.UtcDateTime ?? state.LastModifiedUtc;
                state.ChangeFrequency = pageRef.ChangeFrequency ?? state.ChangeFrequency;
                toIndex.Add(url);
                changedCount++;
            }
            else if (!IsIndexed(state))
            {
                // The page is already tracked but has never been successfully indexed — a prior attempt was
                // queued and then failed (for example the knowledge-base provider was unavailable), so its
                // crawl state still carries the "never indexed" marker. Re-enqueue it so it is retried; the
                // handler stamps LastIndexedUtc once indexing actually succeeds.
                toIndex.Add(url);
                changedCount++;
            }
            else
            {
                unchanged++;
            }

            await _crawlStateStore.UpdateAsync(state, cancellationToken);
        }

        var removed = existingByUrl.Keys
            .Where(url => !discovered.ContainsKey(url))
            .ToArray();

        if (toIndex.Count > 0)
        {
            await _indexingQueue.QueueSyncDataSourceDocumentsAsync(crawler.AIDataSourceId, toIndex, cancellationToken);
        }

        if (removed.Length > 0)
        {
            await _indexingQueue.QueueRemoveDataSourceDocumentsAsync(crawler.AIDataSourceId, removed, cancellationToken);
            await _crawlStateStore.DeleteByUrlsAsync(crawler.ItemId, removed, cancellationToken);
        }

        var result = new WebCrawlerReindexResult(newCount, changedCount, removed.Length, unchanged)
        {
            DiscoveredCount = discovered.Count,
        };

        if (_logger.IsEnabled(LogLevel.Information) && (result.NewCount > 0 || result.ChangedCount > 0 || result.RemovedCount > 0))
        {
            _logger.LogInformation(
                "Web-crawler re-index planned for crawler '{CrawlerId}' → data source '{DataSourceId}': {New} new, {Changed} changed, {Removed} removed, {Unchanged} unchanged.",
                crawler.ItemId,
                crawler.AIDataSourceId,
                result.NewCount,
                result.ChangedCount,
                result.RemovedCount,
                result.UnchangedCount);
        }

        return result;
    }

    private static bool IsIndexed(WebCrawlState state)
    {
        // A never-indexed placeholder carries DateTime.MinValue (year 1). Comparing on the year keeps this
        // robust against time-zone/serialization round-tripping that can shift the stored min value by a few
        // hours without ever reaching a real (post-year-1) indexing timestamp.
        return state.LastIndexedUtc.Year > 1;
    }

    private static bool HasChanged(CrawledPageRef pageRef, WebCrawlState state)
    {
        // With no advertised timestamp there is no reliable change signal, so an already-indexed page is
        // left alone; the periodic full data-source alignment still refreshes it.
        if (pageRef.LastModifiedUtc is null)
        {
            return false;
        }

        return state.LastModifiedUtc is null || pageRef.LastModifiedUtc.Value.UtcDateTime > state.LastModifiedUtc.Value;
    }
}
