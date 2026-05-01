using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Completions;

/// <summary>
/// Provides shared AI completion usage tracking operations for recording and querying
/// provider usage across chat sessions and other completion flows.
/// </summary>
public interface IAICompletionUsageService : IAICompletionUsageObserver
{
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
