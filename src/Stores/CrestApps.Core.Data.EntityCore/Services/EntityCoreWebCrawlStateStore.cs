using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Data.EntityCore.Services;

/// <summary>
/// EntityFramework Core-backed <see cref="IWebCrawlStateStore"/> for web-crawler crawl state. Records are
/// grouped by the owning crawler, stored as their source.
/// </summary>
public sealed class EntityCoreWebCrawlStateStore : SourceDocumentCatalog<WebCrawlState>, IWebCrawlStateStore
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EntityCoreWebCrawlStateStore"/> class.
    /// </summary>
    /// <param name="dbContext">The db context.</param>
    /// <param name="logger">The logger.</param>
    public EntityCoreWebCrawlStateStore(
        CrestAppsEntityDbContext dbContext,
        ILogger<DocumentCatalog<WebCrawlState>> logger = null)
        : base(dbContext, logger)
    {
    }

    /// <inheritdoc />
    public async Task DeleteByCrawlerIdAsync(string webCrawlerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(webCrawlerId);

        var records = await GetTrackedQuery()
            .Where(x => x.Source == webCrawlerId)
            .ToListAsync(cancellationToken);

        if (records.Count == 0)
        {
            return;
        }

        foreach (var record in records)
        {
            if (record.Document is not null)
            {
                DbContext.Documents.Remove(record.Document);
            }
        }

        DbContext.CatalogRecords.RemoveRange(records);
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

        var records = await GetTrackedQuery()
            .Where(x => x.Source == webCrawlerId)
            .ToListAsync(cancellationToken);

        foreach (var record in records)
        {
            var state = CatalogRecordFactory.Materialize<WebCrawlState>(record);

            if (!urlSet.Contains(state.Url))
            {
                continue;
            }

            if (record.Document is not null)
            {
                DbContext.Documents.Remove(record.Document);
            }

            DbContext.CatalogRecords.Remove(record);
        }
    }
}
