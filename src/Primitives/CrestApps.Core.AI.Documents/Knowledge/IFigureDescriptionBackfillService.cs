using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Documents.Knowledge;

/// <summary>
/// Transcribes the figures that ingestion deliberately left untranscribed.
/// </summary>
/// <remarks>
/// Ingestion never waits for a vision model: a five hundred page document would otherwise stay unsearchable
/// until every picture in it had been read. The text is indexed immediately, the figures worth reading are
/// marked pending, and this finishes them afterwards.
/// </remarks>
public interface IFigureDescriptionBackfillService
{
    /// <summary>
    /// Transcribes one batch of pending figures for every ingested data source.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many figures were transcribed.</returns>
    Task<int> BackfillDueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Transcribes one batch of pending figures for one data source.
    /// </summary>
    /// <param name="dataSource">The data source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many figures were transcribed.</returns>
    Task<int> BackfillAsync(AIDataSource dataSource, CancellationToken cancellationToken = default);
}
