using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.AI.DataSources;

/// <summary>
/// Store for managing <see cref="WebCrawler"/> records.
/// </summary>
public interface IWebCrawlerStore : ISourceCatalog<WebCrawler>
{
    /// <summary>
    /// Retrieves every crawler that targets the specified AI data source.
    /// </summary>
    /// <param name="dataSourceId">The target AI data source identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The crawlers pointing at the data source.</returns>
    Task<IReadOnlyCollection<WebCrawler>> GetByDataSourceIdAsync(string dataSourceId, CancellationToken cancellationToken = default);
}
