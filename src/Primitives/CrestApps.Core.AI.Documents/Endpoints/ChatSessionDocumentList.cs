using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Documents.Endpoints;

/// <summary>
/// Keeps the list of documents a chat session carries in step with the documents stored for it. The list is saved with the
/// whole session, so anything that saves an older copy of the session (a chat turn that started before an upload did, or an
/// upload that waited for the lock) can drop documents from it that are still stored under the session id, where nothing
/// could remove them and the cap no longer counted them, or bring back one that has since been removed. The document store
/// is what is true, so the endpoints read it.
/// </summary>
internal static class ChatSessionDocumentList
{
    /// <summary>
    /// Makes the session's list match what is stored for the session, both ways: a stored upload the list does not have is
    /// added, and a list entry that has no stored document is dropped. Files the assistant generated are stored under the
    /// same session id and type but are not uploads, so they are never listed and never counted.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="documentStore">The document store.</param>
    /// <returns>How many entries were added to or dropped from the list.</returns>
    public static async Task<int> ReconcileWithStoreAsync(AIChatSession session, IAIDocumentStore documentStore)
    {
        session.Documents ??= [];

        var stored = (await documentStore.GetDocumentsAsync(session.SessionId, AIReferenceTypes.Document.ChatSession) ?? [])
            .Where(document => !document.Get<bool>(DefaultGeneratedDocumentService.GeneratedPropertyName))
            .ToList();
        var storedIds = stored.Select(document => document.ItemId).ToHashSet(StringComparer.Ordinal);
        var changed = session.Documents.RemoveAll(listed => !storedIds.Contains(listed.DocumentId));

        foreach (var document in stored)
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
            changed++;
        }

        return changed;
    }
}
