using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Ingestion;

/// <summary>
/// Reads and writes back the records of one kind of ingestion source.
/// </summary>
/// <remarks>
/// The ingestion pipeline runs an <see cref="IngestionSource"/> without caring which store it came from, but
/// two things it does need the record itself: recording what a run did, and reading the settings a figure's
/// own source was configured with. Each feature contributes one of these for its own record type, so the
/// pipeline reaches every kind of source without naming any of them and without requiring that a feature a
/// host did not enable be registered.
/// </remarks>
public interface IIngestionSourceProvider
{
    /// <summary>
    /// Finds a record this provider owns.
    /// </summary>
    /// <param name="itemId">The record identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The record, or <see langword="null"/> when this provider does not hold it.</returns>
    Task<IngestionSource> FindByIdAsync(string itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a record, when it is one this provider owns.
    /// </summary>
    /// <param name="ingestionSource">The record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when this provider owns the record and saved it.</returns>
    Task<bool> TryUpdateAsync(IngestionSource ingestionSource, CancellationToken cancellationToken = default);
}
