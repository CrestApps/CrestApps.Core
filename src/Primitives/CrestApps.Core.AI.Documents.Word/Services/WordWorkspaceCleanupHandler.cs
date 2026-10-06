using CrestApps.Core.AI.Documents.Word.Workspace;

namespace CrestApps.Core.AI.Documents.Word.Services;

/// <summary>
/// Deletes a conversation's Word workspace — its working documents — when the conversation is deleted.
/// </summary>
internal sealed class WordWorkspaceCleanupHandler : IConversationWorkspaceCleanupHandler
{
    private readonly IWordWorkspaceStore _workspaceStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="WordWorkspaceCleanupHandler"/> class.
    /// </summary>
    /// <param name="workspaceStore">The workspace store.</param>
    public WordWorkspaceCleanupHandler(IWordWorkspaceStore workspaceStore)
    {
        _workspaceStore = workspaceStore;
    }

    /// <summary>
    /// Deletes the workspace kept for the conversation.
    /// </summary>
    /// <param name="referenceId">The conversation identifier.</param>
    /// <param name="referenceType">The conversation reference type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task CleanupAsync(string referenceId, string referenceType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(referenceId) || string.IsNullOrEmpty(referenceType))
        {
            return Task.CompletedTask;
        }

        return _workspaceStore.DeleteAsync(new WordWorkspaceScope(referenceId, referenceType), cancellationToken);
    }
}
