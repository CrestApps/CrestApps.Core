using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.WebCrawlers;

/// <summary>
/// Gives the ingestion pipeline access to <see cref="WebCrawler"/> records.
/// </summary>
/// <remarks>
/// A crawler pointed at an ingested data source is read by the ingestion pipeline rather than planned for
/// re-indexing, so it produces run summaries and figures exactly as a file source does.
/// </remarks>
public sealed class WebCrawlerIngestionSourceProvider : IIngestionSourceProvider
{
    private readonly IWebCrawlerStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebCrawlerIngestionSourceProvider"/> class.
    /// </summary>
    /// <param name="store">The store web crawlers live in.</param>
    public WebCrawlerIngestionSourceProvider(IWebCrawlerStore store)
    {
        _store = store;
    }

    /// <inheritdoc />
    public async Task<IngestionSource> FindByIdAsync(string itemId, CancellationToken cancellationToken = default)
        => await _store.FindByIdAsync(itemId, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> TryUpdateAsync(IngestionSource ingestionSource, CancellationToken cancellationToken = default)
    {
        if (ingestionSource is not WebCrawler crawler)
        {
            return false;
        }

        await _store.UpdateAsync(crawler, cancellationToken);

        return true;
    }
}
