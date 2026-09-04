#nullable enable
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Chat.Realtime;

/// <summary>
/// Persists completed realtime conversation turns for a session. Abstracting persistence lets the shared
/// <see cref="RealtimeChatSessionRunner"/> record history for both AI Chat sessions
/// (<c>AIChatSessionPrompt</c>) and Chat Interactions (<c>ChatInteractionPrompt</c>) without duplicating
/// the turn-accumulation logic.
/// </summary>
public interface IRealtimeTurnStore
{
    /// <summary>
    /// Persists a user utterance for the session.
    /// </summary>
    /// <remarks>
    /// The turn is created when the provider commits the utterance, which is <em>before</em> its transcription is
    /// available — input-audio transcription lags the assistant's spoken reply, so a turn created on the
    /// transcript would be stamped later than the reply it prompted and would appear underneath it in history.
    /// The text arrives afterwards through <see cref="UpdateUserTurnAsync"/>, keeping the earlier timestamp.
    /// </remarks>
    /// <param name="sessionId">The session or interaction the turn belongs to.</param>
    /// <param name="turnId">A stable id for the turn, used to fill in its text later.</param>
    /// <param name="text">The utterance text, or empty when it is not transcribed yet.</param>
    /// <param name="createdUtc">When the utterance was spoken.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask CreateUserTurnAsync(string sessionId, string turnId, string text, DateTime createdUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Fills in (or corrects) the text of a user turn created earlier by <see cref="CreateUserTurnAsync"/>,
    /// leaving its original timestamp and position in the conversation intact. A turn the store no longer knows
    /// about is ignored.
    /// </summary>
    /// <param name="sessionId">The session or interaction the turn belongs to.</param>
    /// <param name="turnId">The id passed to <see cref="CreateUserTurnAsync"/>.</param>
    /// <param name="text">The transcribed text.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask UpdateUserTurnAsync(string sessionId, string turnId, string text, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a user turn created earlier by <see cref="CreateUserTurnAsync"/> that turned out to carry nothing
    /// worth showing — an utterance the provider never answered, or one whose transcription failed. A turn the
    /// store no longer knows about is ignored.
    /// </summary>
    /// <param name="sessionId">The session or interaction the turn belongs to.</param>
    /// <param name="turnId">The id passed to <see cref="CreateUserTurnAsync"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask DeleteUserTurnAsync(string sessionId, string turnId, CancellationToken cancellationToken);

    /// <summary>
    /// Persists a completed assistant turn (with its citations) for the session.
    /// </summary>
    ValueTask CreateAssistantTurnAsync(
        string sessionId,
        string messageId,
        string content,
        string? title,
        Dictionary<string, AICompletionReference>? references,
        DateTime createdUtc,
        CancellationToken cancellationToken);
}
