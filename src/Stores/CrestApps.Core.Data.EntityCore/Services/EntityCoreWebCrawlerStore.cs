using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Data.EntityCore.Services;

/// <summary>
/// EntityFramework Core-backed <see cref="IWebCrawlerStore"/>. The strategy is the crawler's source; the
/// target data source id is denormalized into the catalog record's reference column so crawlers can be
/// queried per data source.
/// </summary>
public sealed class EntityCoreWebCrawlerStore : SourceDocumentCatalog<WebCrawler>, IWebCrawlerStore
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EntityCoreWebCrawlerStore"/> class.
    /// </summary>
    /// <param name="dbContext">The db context.</param>
    /// <param name="logger">The logger.</param>
    public EntityCoreWebCrawlerStore(
        CrestAppsEntityDbContext dbContext,
        ILogger<DocumentCatalog<WebCrawler>> logger = null)
        : base(dbContext, logger)
    {
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<WebCrawler>> GetByDataSourceIdAsync(string dataSourceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataSourceId);

        var records = await GetReadQuery()
            .Where(x => x.ReferenceId == dataSourceId)
            .ToListAsync(cancellationToken);

        return records.Select(CatalogRecordFactory.Materialize<WebCrawler>).ToArray();
    }
}
