namespace CrestApps.Core.AI.Documents;

/// <summary>
/// Removes a per-conversation working area that a document feature keeps outside the document store — a
/// scratch database, a set of working copies — when the conversation that owns it is deleted.
/// </summary>
/// <remarks>
/// <see cref="IConversationDocumentCleanupService"/> deletes the documents a conversation holds, but a
/// feature that keeps its own working state next to those documents is the only one that knows where that
/// state lives. Registering an implementation lets that feature clean up after itself without the cleanup
/// service having to know every package that might be installed.
/// </remarks>
public interface IConversationWorkspaceCleanupHandler
{
    /// <summary>
    /// Deletes the working state kept for the specified conversation.
    /// </summary>
    /// <param name="referenceId">The owning conversation identifier (the chat session or chat interaction id).</param>
    /// <param name="referenceType">The owning conversation reference type (for example <c>chat-session</c> or <c>chat-interaction</c>).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task CleanupAsync(string referenceId, string referenceType, CancellationToken cancellationToken = default);
}
