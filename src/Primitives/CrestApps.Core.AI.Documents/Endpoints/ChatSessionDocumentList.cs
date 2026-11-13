using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Documents.Endpoints;

/// <summary>
/// Keeps the list of documents a chat session carries in step with the documents stored for it. The list is saved with the
/// whole session, so anything that saves an older copy of the session (a chat turn that started before an upload did) drops
/// documents from it that are still stored under the session id, where nothing could remove them and the cap no longer
/// counted them. The document store is what is true, so the endpoints read it.
/// </summary>
internal static class ChatSessionDocumentList
{
    /// <summary>
    /// Adds to the session's list every document stored for the session that the list does not have.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="documentStore">The document store.</param>
    /// <returns>How many documents were added back to the list.</returns>
    public static async Task<int> RelistStoredDocumentsAsync(AIChatSession session, IAIDocumentStore documentStore)
    {
        session.Documents ??= [];

        var stored = await documentStore.GetDocumentsAsync(session.SessionId, AIReferenceTypes.Document.ChatSession);
        var added = 0;

        foreach (var document in stored ?? [])
        {
            if (session.Documents.Any(listed => string.Equals(listed.DocumentId, document.ItemId, StringComparison.Ordinal)))
            {
                continue;
            }

            session.Documents.Add(new ChatDocumentInfo
            {
                DocumentId = document.ItemId,
                FileName = document.FileName,
                FileSize = document.FileSize,
                ContentType = document.ContentType,
            });
            added++;
        }

        return added;
    }
}
