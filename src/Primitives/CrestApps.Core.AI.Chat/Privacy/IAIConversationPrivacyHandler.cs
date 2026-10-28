using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Chat.Privacy;

/// <summary>
/// Takes part in applying a profile's storage mode to a conversation, and in deleting one, for data a host or feature
/// keeps about a conversation outside the chat session and its prompts, such as evaluations or analytics records.
/// </summary>
public interface IAIConversationPrivacyHandler
{
    /// <summary>
    /// Called first: loads the handler's data about the conversation, keeps it in
    /// <see cref="AIConversationPrivacyContext.Items"/>, and adds its free text to
    /// <see cref="AIConversationPrivacyContext.Texts"/> so the personal details in it are found.
    /// </summary>
    /// <param name="context">The conversation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the data is collected.</returns>
    Task CollectingAsync(AIConversationPrivacyContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Called once the personal details are found: redacts the handler's data with
    /// <see cref="AIConversationPrivacyContext.Redact(string)"/>, or clears it for
    /// <see cref="AIDataStorageMode.MetadataOnly"/>, and saves it.
    /// </summary>
    /// <param name="context">The conversation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the data is changed.</returns>
    Task ApplyingAsync(AIConversationPrivacyContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Called before a conversation is deleted: deletes the handler's data about it.
    /// </summary>
    /// <param name="session">The chat session being deleted.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the data is deleted.</returns>
    Task DeletingAsync(AIChatSession session, CancellationToken cancellationToken);
}
