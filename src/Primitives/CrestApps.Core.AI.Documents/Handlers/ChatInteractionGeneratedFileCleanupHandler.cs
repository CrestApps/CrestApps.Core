using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Documents.Handlers;

/// <summary>
/// Removes AI-generated downloadable files that were attached to a chat interaction's messages when the
/// interaction's history is cleared, so generated exports do not linger in the document file store after
/// the messages that produced them are gone. Uploaded source documents are left untouched.
/// </summary>
internal sealed class ChatInteractionGeneratedFileCleanupHandler : IChatInteractionHistoryHandler
{
    private readonly IConversationDocumentCleanupService _cleanupService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatInteractionGeneratedFileCleanupHandler"/> class.
    /// </summary>
    /// <param name="cleanupService">The conversation document cleanup service.</param>
    public ChatInteractionGeneratedFileCleanupHandler(IConversationDocumentCleanupService cleanupService)
    {
        _cleanupService = cleanupService;
    }

    /// <summary>
    /// Deletes the generated files referenced by the cleared messages.
    /// </summary>
    /// <param name="interaction">The chat interaction whose history was cleared.</param>
    /// <param name="clearedPrompts">The prompts (messages) that were removed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task HistoryClearedAsync(
        ChatInteraction interaction,
        IReadOnlyCollection<ChatInteractionPrompt> clearedPrompts,
        CancellationToken cancellationToken = default)
    {
        if (clearedPrompts is null || clearedPrompts.Count == 0)
        {
            return;
        }

        var documentIds = clearedPrompts
            .Where(prompt => prompt.References is not null)
            .SelectMany(prompt => prompt.References.Values)
            .Where(reference => reference is not null
                && reference.IsGenerated
                && !string.IsNullOrEmpty(reference.ReferenceId))
            .Select(reference => reference.ReferenceId);

        await _cleanupService.CleanupGeneratedDocumentsAsync(documentIds, cancellationToken);
    }
}
