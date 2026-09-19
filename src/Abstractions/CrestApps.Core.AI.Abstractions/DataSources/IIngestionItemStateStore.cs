using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.AI.DataSources;

/// <summary>
/// Persists the per-item state (<see cref="IngestionItemState"/>) an ingestion run leaves behind, so the
/// next run reads only what changed. Records are grouped by the owning record, which is stored as their
/// source, so <see cref="ISourceCatalog{T}.GetAsync(string, System.Threading.CancellationToken)"/> retrieves
/// every item of one source.
/// </summary>
/// <remarks>
/// This holds the state of records run through the ingestion pipeline: every <see cref="FileSource"/>, and
/// any <see cref="WebCrawler"/> pointed at an ingested data source. A crawler that feeds a <c>Web</c> data
/// source is driven by the re-index service instead and keeps its state in
/// <see cref="IWebCrawlStateStore"/>.
/// </remarks>
public interface IIngestionItemStateStore : ISourceCatalog<IngestionItemState>
{
    /// <summary>
    /// Deletes every item-state record for the specified source.
    /// </summary>
    /// <param name="sourceId">The owning record's identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task DeleteBySourceIdAsync(string sourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the item-state records for the specified items within one source.
    /// </summary>
    /// <param name="sourceId">The owning record's identifier.</param>
    /// <param name="itemKeys">The items whose state should be removed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task DeleteByItemKeysAsync(string sourceId, IEnumerable<string> itemKeys, CancellationToken cancellationToken = default);
}
