using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Documents.Pdf.Services;

/// <summary>
/// Deletes a chat interaction's PDF workspace when its history is cleared, so the next conversation starts
/// from the uploaded files rather than from documents built in one the user asked to forget.
/// </summary>
internal sealed class PdfWorkspaceHistoryClearedHandler : IChatInteractionHistoryHandler
{
    private readonly IPdfWorkspaceStore _workspaceStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfWorkspaceHistoryClearedHandler"/> class.
    /// </summary>
    /// <param name="workspaceStore">The workspace store.</param>
    public PdfWorkspaceHistoryClearedHandler(IPdfWorkspaceStore workspaceStore)
    {
        _workspaceStore = workspaceStore;
    }

    /// <summary>
    /// Deletes the workspace.
    /// </summary>
    /// <param name="interaction">The chat interaction whose history was cleared.</param>
    /// <param name="clearedPrompts">The prompts that were removed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task HistoryClearedAsync(
        ChatInteraction interaction,
        IReadOnlyCollection<ChatInteractionPrompt> clearedPrompts,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(interaction?.ItemId))
        {
            return Task.CompletedTask;
        }

        return _workspaceStore.DeleteAsync(
            new PdfWorkspaceScope(interaction.ItemId, AIReferenceTypes.Document.ChatInteraction),
            cancellationToken);
    }
}
