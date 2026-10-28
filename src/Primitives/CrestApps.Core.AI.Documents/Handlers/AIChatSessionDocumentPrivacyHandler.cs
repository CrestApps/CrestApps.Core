using CrestApps.Core.AI.Chat.Privacy;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Documents.Handlers;

/// <summary>
/// Deletes a chat session's documents when the privacy service deletes the conversation past its retention.
/// </summary>
internal sealed class AIChatSessionDocumentPrivacyHandler : AIConversationPrivacyHandlerBase
{
    private readonly IConversationDocumentCleanupService _cleanupService;

    public AIChatSessionDocumentPrivacyHandler(IConversationDocumentCleanupService cleanupService)
    {
        _cleanupService = cleanupService;
    }

    public override async Task DeletingAsync(AIChatSession session, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(session?.SessionId))
        {
            await _cleanupService.CleanupAsync(session.SessionId, AIReferenceTypes.Document.ChatSession, cancellationToken);
        }
    }
}
