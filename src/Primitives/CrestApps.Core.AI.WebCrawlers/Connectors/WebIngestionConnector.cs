using System.Text;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.WebCrawlers.Strategies;
using CrestApps.Core.Models;

namespace CrestApps.Core.AI.WebCrawlers.Connectors;

/// <summary>
/// Presents a web-crawl strategy as an ingestion connector.
/// </summary>
/// <remarks>
/// The crawler subsystem was already a connector with <c>Web</c> in its names: it discovers what exists,
/// fetches one item, and tracks what changed. This adapter says so, so that a folder, a blob container and a
/// website reach the same reader, the same enrichment and the same store without any of them knowing about
/// the others. Nothing about existing crawlers changes.
/// </remarks>
public sealed class WebIngestionConnector : IIngestionConnector
{
    private readonly IWebCrawlerStrategy _strategy;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebIngestionConnector"/> class.
    /// </summary>
    /// <param name="strategy">The strategy being adapted.</param>
    public WebIngestionConnector(IWebCrawlerStrategy strategy)
    {
        _strategy = strategy;
    }

    /// <inheritdoc />
    public string Name => _strategy.Name;

    /// <inheritdoc />
    public ValueTask ValidateAsync(WebCrawler settings, ValidationResultDetails result, CancellationToken cancellationToken = default)
    {
        return _strategy.ValidateAsync(settings, result, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IngestionDiscoveryResult> DiscoverAsync(WebCrawler settings, string continuationToken = null, CancellationToken cancellationToken = default)
    {
        var discovery = await _strategy.DiscoverDetailedAsync(settings, cancellationToken);
        var pages = discovery.Pages;

        if (pages is null || pages.Count == 0)
        {
            // A sitemap that returned nothing is a sitemap that could not be read, not a site with no
            // pages. Saying the listing is incomplete is what stops every page being tombstoned.
            return new IngestionDiscoveryResult([], IsComplete: false, discovery.Message ?? "Discovery returned no pages.");
        }

        var items = new List<IngestionItemRef>(pages.Count);

        foreach (var page in pages)
        {
            items.Add(new IngestionItemRef(
                page.Url,
                page.LastModifiedUtc?.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                SizeBytes: null,
                page.LastModifiedUtc));
        }

        return new IngestionDiscoveryResult(items, discovery.IsComplete, discovery.Message);
    }

    /// <inheritdoc />
    public async Task<IngestionItemContent> FetchAsync(WebCrawler settings, string itemId, CancellationToken cancellationToken = default)
    {
        var page = await _strategy.FetchAsync(settings, itemId, cancellationToken);

        if (page is null || string.IsNullOrWhiteSpace(page.Content))
        {
            return null;
        }

        return new IngestionItemContent
        {
            Content = new MemoryStream(Encoding.UTF8.GetBytes(page.Content)),
            MediaType = "text/plain",
            Title = page.Title,
            FileName = itemId,
        };
    }
}
