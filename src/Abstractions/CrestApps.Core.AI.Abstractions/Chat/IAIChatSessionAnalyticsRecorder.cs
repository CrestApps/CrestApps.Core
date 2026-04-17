using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Chat;

/// <summary>
/// Persists host-specific chat session analytics after shared post-close analysis
/// has determined the final session metrics and resolution status.
/// </summary>
public interface IAIChatSessionAnalyticsRecorder
{
    /// <summary>
    /// Records end-of-session analytics for the specified chat session.
    /// </summary>
    /// <param name="profile">The AI profile associated with the session.</param>
    /// <param name="session">The chat session to record analytics for.</param>
    /// <param name="prompts">The prompts exchanged during the session.</param>
    /// <param name="isResolved">Whether the session was resolved.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task RecordSessionEndedAsync(
        AIProfile profile,
        AIChatSession session,
        IReadOnlyList<AIChatSessionPrompt> prompts,
        bool isResolved,
        CancellationToken cancellationToken = default);
}
