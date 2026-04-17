using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Chat;

/// <summary>
/// Persists host-specific conversion-goal evaluation results after the shared
/// post-close processor evaluates the configured goals.
/// </summary>
public interface IAIChatSessionConversionGoalRecorder
{
    /// <summary>
    /// Records evaluated conversion-goal results for the specified chat session.
    /// </summary>
    /// <param name="profile">The AI profile associated with the session.</param>
    /// <param name="session">The chat session to record goals for.</param>
    /// <param name="goalResults">The evaluated conversion-goal results.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task RecordConversionGoalsAsync(
        AIProfile profile,
        AIChatSession session,
        IReadOnlyList<ConversionGoalResult> goalResults,
        CancellationToken cancellationToken = default);
}
