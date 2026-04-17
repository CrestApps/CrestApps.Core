using CrestApps.Core.Infrastructure.Indexing.Models;

namespace CrestApps.Core.Infrastructure.Indexing.DataSources;

/// <summary>
/// Reads documents from a source index. Implementations are keyed by provider name.
/// </summary>
public interface IDataSourceDocumentReader
{
    /// <summary>
    /// Reads documents from the specified source index in batches.
    /// </summary>
    /// <param name="indexProfile">The index profile describing the source index.</param>
    /// <param name="keyFieldName">The name of the field used as the document key.</param>
    /// <param name="titleFieldName">The name of the field containing the document title.</param>
    /// <param name="contentFieldName">The name of the field containing the document content.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    IAsyncEnumerable<KeyValuePair<string, SourceDocument>> ReadAsync(
        IIndexProfileInfo indexProfile,
        string keyFieldName,
        string titleFieldName,
        string contentFieldName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads specific documents from the source index by their native document IDs.
    /// </summary>
    /// <param name="indexProfile">The index profile describing the source index.</param>
    /// <param name="documentIds">The native document IDs to retrieve.</param>
    /// <param name="keyFieldName">The name of the field used as the document key.</param>
    /// <param name="titleFieldName">The name of the field containing the document title.</param>
    /// <param name="contentFieldName">The name of the field containing the document content.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    IAsyncEnumerable<KeyValuePair<string, SourceDocument>> ReadByIdsAsync(
        IIndexProfileInfo indexProfile,
        IEnumerable<string> documentIds,
        string keyFieldName,
        string titleFieldName,
        string contentFieldName,
        CancellationToken cancellationToken = default);
}
