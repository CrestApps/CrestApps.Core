using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Chat;

/// <summary>
/// Handles lifecycle events raised when a <see cref="ChatInteraction"/>'s message history is cleared
/// from the client (e.g., SignalR hub).
/// </summary>
/// <remarks>
/// Implementations can release resources tied to the cleared messages, such as removing AI-generated
/// downloadable files that were attached to those messages so nothing is left orphaned in storage once
/// the messages that produced them are gone.
/// </remarks>
public interface IChatInteractionHistoryHandler
{
    /// <summary>
    /// Called after a chat interaction's message history has been cleared.
    /// </summary>
    /// <param name="interaction">The <see cref="ChatInteraction"/> whose history was cleared.</param>
    /// <param name="clearedPrompts">The prompts (messages) that were removed.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task HistoryClearedAsync(
        ChatInteraction interaction,
        IReadOnlyCollection<ChatInteractionPrompt> clearedPrompts,
        CancellationToken cancellationToken = default);
}
