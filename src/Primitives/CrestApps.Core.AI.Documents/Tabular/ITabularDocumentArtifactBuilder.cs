namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Builds a parsed tabular artifact directly from a stored source file for a specific extension.
/// Implementations can use a format-specific fast path to avoid generic document-ingestion overhead.
/// </summary>
public interface ITabularDocumentArtifactBuilder
{
    /// <summary>
    /// Creates a parsed tabular artifact from the supplied file stream.
    /// </summary>
    /// <param name="source">The source file stream.</param>
    /// <param name="fileName">The original file name.</param>
    /// <param name="contentType">The source content type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The parsed tabular artifact.</returns>
    Task<TabularDocumentArtifact> CreateAsync(
        Stream source,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);
}
