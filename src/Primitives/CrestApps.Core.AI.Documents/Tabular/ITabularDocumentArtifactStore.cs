namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Stores parsed tabular document artifacts in a durable location shared by application instances.
/// </summary>
public interface ITabularDocumentArtifactStore
{
    /// <summary>
    /// Gets a parsed tabular artifact for a document.
    /// </summary>
    /// <param name="documentId">The document identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<TabularDocumentArtifact> GetAsync(string documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a parsed tabular artifact for a document.
    /// </summary>
    /// <param name="documentId">The document identifier.</param>
    /// <param name="artifact">The parsed tabular artifact.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task SaveAsync(
        string documentId,
        TabularDocumentArtifact artifact,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a parsed tabular artifact for a document.
    /// </summary>
    /// <param name="documentId">The document identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task DeleteAsync(string documentId, CancellationToken cancellationToken = default);
}
