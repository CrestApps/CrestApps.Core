using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using CrestApps.Core;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.WebCrawlers.Strategies;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;

namespace CrestApps.Core.AI.WebCrawlers;

/// <summary>
/// The <c>Web</c> AI data source source handler. A <c>Web</c> data source is a target bucket that
/// aggregates every enabled <see cref="WebCrawler"/> pointing at it: a full read runs each crawler's
/// strategy (discover then fetch) and yields one source document per page, keyed by the page URL, which
/// becomes the knowledge-base reference id used for citations.
/// </summary>
public sealed class WebAIDataSourceSourceHandler : IAIDataSourceSourceHandler
{
    private readonly IWebCrawlerStore _crawlerStore;
    private readonly IWebCrawlStateStore _crawlStateStore;
    private readonly IWebCrawlerStrategyResolver _strategyResolver;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebAIDataSourceSourceHandler"/> class.
    /// </summary>
    /// <param name="crawlerStore">The crawler store.</param>
    /// <param name="crawlStateStore">The crawl-state store.</param>
    /// <param name="strategyResolver">The strategy resolver.</param>
    /// <param name="timeProvider">The time provider.</param>
    public WebAIDataSourceSourceHandler(
        IWebCrawlerStore crawlerStore,
        IWebCrawlStateStore crawlStateStore,
        IWebCrawlerStrategyResolver strategyResolver,
        TimeProvider timeProvider)
    {
        _crawlerStore = crawlerStore;
        _crawlStateStore = crawlStateStore;
        _strategyResolver = strategyResolver;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public string SourceType => AIDataSourceSourceTypes.Web;

    /// <inheritdoc />
    public ValueTask<string> GetReferenceTypeAsync(AIDataSource dataSource, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(SourceType);
    }

    /// <inheritdoc />
    public ValueTask ValidateAsync(AIDataSource dataSource, ValidationResultDetails result, CancellationToken cancellationToken = default)
    {
        // A Web data source needs no source-specific settings; the sites to scrape are managed as separate
        // web-crawler records that point at it.
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<KeyValuePair<string, SourceDocument>> ReadAsync(
        AIDataSource dataSource,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        var crawlers = await _crawlerStore.GetByDataSourceIdAsync(dataSource.ItemId, cancellationToken);

        foreach (var crawler in crawlers.Where(crawler => crawler.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var strategy = _strategyResolver.Get(crawler.Source);

            if (strategy is null)
            {
                continue;
            }

            // A full read is authoritative: reset the crawler's state and rebuild it as pages are indexed.
            await _crawlStateStore.DeleteByCrawlerIdAsync(crawler.ItemId, cancellationToken);

            var refs = await strategy.DiscoverAsync(crawler, cancellationToken);

            foreach (var pageRef in refs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var page = await strategy.FetchAsync(crawler, pageRef.Url, cancellationToken);

                if (page is null)
                {
                    continue;
                }

                await _crawlStateStore.CreateAsync(BuildState(crawler.ItemId, pageRef.Url, pageRef.LastModifiedUtc?.UtcDateTime, pageRef.ChangeFrequency, page.Content), cancellationToken);

                yield return CreateDocument(pageRef.Url, page);
            }
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<KeyValuePair<string, SourceDocument>> ReadByIdsAsync(
        AIDataSource dataSource,
        IEnumerable<string> documentIds,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        var urls = documentIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        if (urls.Length == 0)
        {
            yield break;
        }

        var urlSet = new HashSet<string>(urls, StringComparer.OrdinalIgnoreCase);
        var crawlers = await _crawlerStore.GetByDataSourceIdAsync(dataSource.ItemId, cancellationToken);

        // Map each requested URL to the crawler that owns it (via its recorded crawl state).
        var owners = new Dictionary<string, (WebCrawler Crawler, WebCrawlState State)>(StringComparer.OrdinalIgnoreCase);

        foreach (var crawler in crawlers.Where(crawler => crawler.Enabled))
        {
            var states = await _crawlStateStore.GetAsync(crawler.ItemId, cancellationToken);

            foreach (var state in states)
            {
                if (urlSet.Contains(state.Url))
                {
                    owners.TryAdd(state.Url, (crawler, state));
                }
            }
        }

        foreach (var url in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!owners.TryGetValue(url, out var owner))
            {
                continue;
            }

            var strategy = _strategyResolver.Get(owner.Crawler.Source);

            if (strategy is null)
            {
                continue;
            }

            var page = await strategy.FetchAsync(owner.Crawler, url, cancellationToken);

            if (page is null)
            {
                continue;
            }

            owner.State.ContentHash = ComputeHash(page.Content);
            owner.State.LastIndexedUtc = _timeProvider.GetUtcNow().UtcDateTime;
            owner.State.LastSeenUtc = owner.State.LastIndexedUtc;
            await _crawlStateStore.UpdateAsync(owner.State, cancellationToken);

            yield return CreateDocument(url, page);
        }
    }

    private WebCrawlState BuildState(string crawlerId, string url, DateTime? lastModifiedUtc, string changeFrequency, string content)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        return new WebCrawlState
        {
            ItemId = UniqueId.GenerateId(),
            Source = crawlerId,
            Url = url,
            LastModifiedUtc = lastModifiedUtc,
            ChangeFrequency = changeFrequency,
            ContentHash = ComputeHash(content),
            LastIndexedUtc = now,
            LastSeenUtc = now,
        };
    }

    private static KeyValuePair<string, SourceDocument> CreateDocument(string url, CrawledPage page)
    {
        var fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            [WebCrawlerConstants.UrlFieldName] = url,
        };

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            fields[WebCrawlerConstants.HostFieldName] = uri.Host;
        }

        return new KeyValuePair<string, SourceDocument>(
            url,
            new SourceDocument
            {
                Title = page.Title,
                Content = page.Content,
                Fields = fields,
            });
    }

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content ?? string.Empty));

        return Convert.ToHexStringLower(bytes);
    }
}
