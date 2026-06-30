namespace CrestApps.Core.AI.Documents;

/// <summary>
/// Removes every document attached to a conversation (chat session or chat interaction) when that
/// conversation is deleted. This includes AI-generated, downloadable files (such as exported tabular
/// data) along with their stored file content, tabular artifacts, and document chunks so no orphaned
/// data remains in the document file store once the owning conversation is gone.
/// </summary>
public interface IConversationDocumentCleanupService
{
    /// <summary>
    /// Deletes all documents and their stored content associated with the specified conversation.
    /// </summary>
    /// <param name="referenceId">The owning conversation identifier (the chat session or chat interaction id).</param>
    /// <param name="referenceType">The owning conversation reference type (for example <c>chat-session</c> or <c>chat-interaction</c>).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task CleanupAsync(string referenceId, string referenceType, CancellationToken cancellationToken = default);
}
