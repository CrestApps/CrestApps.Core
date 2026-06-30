using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Support;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.Services;

/// <summary>
/// Default <see cref="IConversationDocumentCleanupService"/> that removes a conversation's documents from
/// the <see cref="IAIDocumentStore"/>, deletes their stored file content from the <see cref="IDocumentFileStore"/>,
/// drops any persisted tabular artifact via the <see cref="ITabularDocumentArtifactStore"/>, and clears the
/// associated chunks from the <see cref="IAIDocumentChunkStore"/>.
/// </summary>
public sealed class DefaultConversationDocumentCleanupService : IConversationDocumentCleanupService
{
    private readonly IAIDocumentStore _documentStore;
    private readonly IAIDocumentChunkStore _chunkStore;
    private readonly IDocumentFileStore _fileStore;
    private readonly ITabularDocumentArtifactStore _artifactStore;
    private readonly ILogger<DefaultConversationDocumentCleanupService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultConversationDocumentCleanupService"/> class.
    /// </summary>
    /// <param name="documentStore">The document metadata store.</param>
    /// <param name="chunkStore">The document chunk store.</param>
    /// <param name="fileStore">The document file store.</param>
    /// <param name="artifactStore">The tabular document artifact store.</param>
    /// <param name="logger">The logger.</param>
    public DefaultConversationDocumentCleanupService(
        IAIDocumentStore documentStore,
        IAIDocumentChunkStore chunkStore,
        IDocumentFileStore fileStore,
        ITabularDocumentArtifactStore artifactStore,
        ILogger<DefaultConversationDocumentCleanupService> logger)
    {
        _documentStore = documentStore;
        _chunkStore = chunkStore;
        _fileStore = fileStore;
        _artifactStore = artifactStore;
        _logger = logger;
    }

    /// <summary>
    /// Deletes all documents and their stored content associated with the specified conversation.
    /// </summary>
    /// <param name="referenceId">The owning conversation identifier (the chat session or chat interaction id).</param>
    /// <param name="referenceType">The owning conversation reference type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task CleanupAsync(string referenceId, string referenceType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(referenceId) || string.IsNullOrEmpty(referenceType))
        {
            return;
        }

        var documents = await _documentStore.GetDocumentsAsync(referenceId, referenceType);

        if (documents.Count == 0)
        {
            return;
        }

        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await DeleteDocumentAsync(document, cancellationToken);
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Removed {DocumentCount} document(s) for conversation '{ReferenceId}' of type '{ReferenceType}'.",
                documents.Count,
                referenceId.SanitizeForLog(),
                referenceType.SanitizeForLog());
        }
    }

    /// <summary>
    /// Deletes the specified AI-generated documents and their stored content. Uploaded source documents
    /// are never removed because only documents flagged as generated are deleted.
    /// </summary>
    /// <param name="documentIds">The identifiers of the generated documents to remove.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task CleanupGeneratedDocumentsAsync(IEnumerable<string> documentIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var removed = 0;

        foreach (var documentId in documentIds)
        {
            if (string.IsNullOrEmpty(documentId) || !seen.Add(documentId))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var document = await _documentStore.FindByIdAsync(documentId, cancellationToken);

            if (document is null || !document.Get<bool>(DefaultGeneratedDocumentService.GeneratedPropertyName))
            {
                continue;
            }

            await DeleteDocumentAsync(document, cancellationToken);
            removed++;
        }

        if (removed > 0 && _logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Removed {DocumentCount} generated document(s).", removed);
        }
    }

    private async Task DeleteDocumentAsync(AIDocument document, CancellationToken cancellationToken)
    {
        await _chunkStore.DeleteByDocumentIdAsync(document.ItemId);
        await _artifactStore.DeleteAsync(document.ItemId, cancellationToken);

        if (!string.IsNullOrWhiteSpace(document.StoredFilePath))
        {
            await _fileStore.DeleteFileAsync(document.StoredFilePath);
        }

        await _documentStore.DeleteAsync(document, cancellationToken);
    }
}
