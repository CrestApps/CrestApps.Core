using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Documents.Word.Workspace;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Documents.Word.Services;

/// <summary>
/// Deletes a chat interaction's Word workspace when its history is cleared, so the next conversation starts
/// from the uploaded files rather than from documents built in one the user asked to forget.
/// </summary>
internal sealed class WordWorkspaceHistoryClearedHandler : IChatInteractionHistoryHandler
{
    private readonly IWordWorkspaceStore _workspaceStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="WordWorkspaceHistoryClearedHandler"/> class.
    /// </summary>
    /// <param name="workspaceStore">The workspace store.</param>
    public WordWorkspaceHistoryClearedHandler(IWordWorkspaceStore workspaceStore)
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
            new WordWorkspaceScope(interaction.ItemId, AIReferenceTypes.Document.ChatInteraction),
            cancellationToken);
    }
}
