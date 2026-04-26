using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Completions;

/// <summary>
/// Observes AI completion usage records so they can be persisted, forwarded, or analyzed.
/// </summary>
public interface IAICompletionUsageObserver
{
    /// <summary>
    /// Called after a completion usage record has been captured.
    /// </summary>
    /// <param name="record">The usage record describing the completed request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task UsageRecordedAsync(AICompletionUsageRecord record, CancellationToken cancellationToken = default);
}
