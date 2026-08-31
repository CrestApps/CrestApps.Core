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
    /// Persists a completed user utterance for the session.
    /// </summary>
    ValueTask CreateUserTurnAsync(string sessionId, string text, DateTime createdUtc, CancellationToken cancellationToken);

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
