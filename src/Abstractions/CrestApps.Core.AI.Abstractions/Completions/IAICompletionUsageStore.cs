using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Completions;

/// <summary>
/// Persists AI completion usage records for reporting, auditing, and analytics.
/// </summary>
public interface IAICompletionUsageStore
{
    /// <summary>
    /// Saves a usage record.
    /// </summary>
    /// <param name="record">The usage record to persist.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task SaveAsync(
        AICompletionUsageRecord record,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves usage records captured within the optional UTC date range.
    /// </summary>
    /// <param name="startDateUtc">The inclusive UTC start date filter.</param>
    /// <param name="endDateUtc">The inclusive UTC end date filter.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The matching usage records ordered by creation time descending.</returns>
    Task<IReadOnlyList<AICompletionUsageRecord>> GetAsync(
        DateTime? startDateUtc,
        DateTime? endDateUtc,
        CancellationToken cancellationToken = default);
}
