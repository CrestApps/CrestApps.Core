using CrestApps.Core.AI.Models;
using CrestApps.Core.Handlers;
using CrestApps.Core.Models;

namespace CrestApps.Core.AI.Documents.Handlers;

/// <summary>
/// Removes a chat interaction's documents (including AI-generated downloadable files) and their stored
/// content once the interaction is deleted, so nothing is left orphaned in the document file store.
/// </summary>
internal sealed class ChatInteractionDocumentCleanupHandler : CatalogEntryHandlerBase<ChatInteraction>
{
    private readonly IConversationDocumentCleanupService _cleanupService;

    public ChatInteractionDocumentCleanupHandler(IConversationDocumentCleanupService cleanupService)
    {
        _cleanupService = cleanupService;
    }

    public override async Task DeletedAsync(DeletedContext<ChatInteraction> context, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(context.Model?.ItemId))
        {
            await _cleanupService.CleanupAsync(context.Model.ItemId, AIReferenceTypes.Document.ChatInteraction, cancellationToken);
        }
    }
}
