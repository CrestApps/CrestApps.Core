using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Services;

/// <summary>
/// Deletes a conversation's PDF workspace — its working documents and pictures — when the conversation is
/// deleted.
/// </summary>
internal sealed class PdfWorkspaceCleanupHandler : IConversationWorkspaceCleanupHandler
{
    private readonly IPdfWorkspaceStore _workspaceStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfWorkspaceCleanupHandler"/> class.
    /// </summary>
    /// <param name="workspaceStore">The workspace store.</param>
    public PdfWorkspaceCleanupHandler(IPdfWorkspaceStore workspaceStore)
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

        return _workspaceStore.DeleteAsync(new PdfWorkspaceScope(referenceId, referenceType), cancellationToken);
    }
}
