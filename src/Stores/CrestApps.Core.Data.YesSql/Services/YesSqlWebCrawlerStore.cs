using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql.Indexes.WebCrawlers;
using Microsoft.Extensions.Options;
using YesSql;

namespace CrestApps.Core.Data.YesSql.Services;

/// <summary>
/// YesSql-backed <see cref="IWebCrawlerStore"/>.
/// </summary>
public sealed class YesSqlWebCrawlerStore : DocumentCatalog<WebCrawler, WebCrawlerIndex>, IWebCrawlerStore
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YesSqlWebCrawlerStore"/> class.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="options">The options.</param>
    public YesSqlWebCrawlerStore(
        ISession session,
        IOptions<YesSqlStoreOptions> options)
        : base(session, options.Value.AICollectionName)
    {
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyCollection<WebCrawler>> GetAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);

        return (await Session.Query<WebCrawler, WebCrawlerIndex>(x => x.Source == source, collection: CollectionName).ListAsync(cancellationToken)).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<WebCrawler>> GetByDataSourceIdAsync(string dataSourceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataSourceId);

        return (await Session.Query<WebCrawler, WebCrawlerIndex>(x => x.AIDataSourceId == dataSourceId, collection: CollectionName).ListAsync(cancellationToken)).ToArray();
    }
}
