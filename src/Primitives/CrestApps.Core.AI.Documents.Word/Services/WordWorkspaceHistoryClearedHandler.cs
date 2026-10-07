using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Documents.Word.Workspace;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.Word.Services;

/// <summary>
/// Deletes a chat interaction's Word workspace when its history is cleared, so the next conversation starts
/// from the uploaded files rather than from documents built in one the user asked to forget. The page pictures
/// its previews showed go with it.
/// </summary>
/// <remarks>
/// A failure is logged rather than thrown: the history is already cleared when this runs, and the handlers after
/// this one, and the clear itself, still have to finish.
/// </remarks>
internal sealed class WordWorkspaceHistoryClearedHandler : IChatInteractionHistoryHandler
{
    private readonly IWordWorkspaceStore _workspaceStore;
    private readonly IEnumerable<IConversationDocumentCleanupService> _documentCleanupServices;
    private readonly ILogger<WordWorkspaceHistoryClearedHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WordWorkspaceHistoryClearedHandler"/> class.
    /// </summary>
    /// <param name="workspaceStore">The workspace store.</param>
    /// <param name="documentCleanupServices">The services that delete generated documents, if any is registered.</param>
    /// <param name="logger">The logger.</param>
    public WordWorkspaceHistoryClearedHandler(
        IWordWorkspaceStore workspaceStore,
        IEnumerable<IConversationDocumentCleanupService> documentCleanupServices,
        ILogger<WordWorkspaceHistoryClearedHandler> logger)
    {
        _workspaceStore = workspaceStore;
        _documentCleanupServices = documentCleanupServices;
        _logger = logger;
    }

    /// <summary>
    /// Deletes the workspace and the preview pictures it recorded.
    /// </summary>
    /// <param name="interaction">The chat interaction whose history was cleared.</param>
    /// <param name="clearedPrompts">The prompts that were removed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task HistoryClearedAsync(
        ChatInteraction interaction,
        IReadOnlyCollection<ChatInteractionPrompt> clearedPrompts,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(interaction?.ItemId))
        {
            return;
        }

        try
        {
            var deleted = await _workspaceStore.DeleteAsync(
                new WordWorkspaceScope(interaction.ItemId, AIReferenceTypes.Document.ChatInteraction),
                cancellationToken);

            if (deleted?.PreviewDocumentIds.Count > 0)
            {
                foreach (var cleanupService in _documentCleanupServices)
                {
                    await cleanupService.CleanupGeneratedDocumentsAsync(deleted.PreviewDocumentIds, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The Word workspace of chat interaction '{InteractionId}' could not be deleted when its history was cleared.", interaction.ItemId);
        }
    }
}
