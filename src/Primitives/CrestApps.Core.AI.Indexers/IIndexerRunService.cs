using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Indexers;

/// <summary>
/// Runs one indexer: list what the source holds, ingest what is new or changed, and remove what is gone.
/// </summary>
public interface IIndexerRunService
{
    /// <summary>
    /// Runs one indexer.
    /// </summary>
    /// <param name="indexer">The configured indexer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the run did.</returns>
    Task<IndexerRunSummary> RunAsync(WebCrawler indexer, CancellationToken cancellationToken = default);
}
