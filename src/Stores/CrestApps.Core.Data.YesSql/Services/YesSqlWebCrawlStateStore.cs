using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql.Indexes.WebCrawlers;
using Microsoft.Extensions.Options;
using YesSql;

namespace CrestApps.Core.Data.YesSql.Services;

/// <summary>
/// YesSql-backed <see cref="IWebCrawlStateStore"/> for web-crawler crawl state.
/// </summary>
public sealed class YesSqlWebCrawlStateStore : DocumentCatalog<WebCrawlState, WebCrawlStateIndex>, IWebCrawlStateStore
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YesSqlWebCrawlStateStore"/> class.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="options">The options.</param>
    public YesSqlWebCrawlStateStore(
        ISession session,
        IOptions<YesSqlStoreOptions> options)
        : base(session, options.Value.AICollectionName)
    {
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyCollection<WebCrawlState>> GetAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);

        return (await Session.Query<WebCrawlState, WebCrawlStateIndex>(x => x.Source == source, collection: CollectionName).ListAsync(cancellationToken)).ToArray();
    }

    /// <inheritdoc />
    public async Task DeleteByCrawlerIdAsync(string webCrawlerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(webCrawlerId);

        var states = await Session.Query<WebCrawlState, WebCrawlStateIndex>(x => x.Source == webCrawlerId, collection: CollectionName).ListAsync(cancellationToken);

        foreach (var state in states)
        {
            Session.Delete(state, CollectionName);
        }
    }

    /// <inheritdoc />
    public async Task DeleteByUrlsAsync(string webCrawlerId, IEnumerable<string> urls, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(webCrawlerId);
        ArgumentNullException.ThrowIfNull(urls);

        var urlSet = new HashSet<string>(urls.Where(url => !string.IsNullOrWhiteSpace(url)), StringComparer.OrdinalIgnoreCase);

        if (urlSet.Count == 0)
        {
            return;
        }

        var states = await Session.Query<WebCrawlState, WebCrawlStateIndex>(x => x.Source == webCrawlerId, collection: CollectionName).ListAsync(cancellationToken);

        foreach (var state in states)
        {
            if (urlSet.Contains(state.Url))
            {
                Session.Delete(state, CollectionName);
            }
        }
    }
}
