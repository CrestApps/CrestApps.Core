using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;

namespace CrestApps.Core.AI.DataSources;

/// <summary>
/// Validates and reads source documents for one AI data source source type.
/// </summary>
public interface IAIDataSourceSourceHandler
{
    /// <summary>
    /// Gets the source type identifier handled by this implementation.
    /// </summary>
    string SourceType { get; }

    /// <summary>
    /// Validates the source configuration for the provided AI data source.
    /// </summary>
    /// <param name="dataSource">The AI data source to validate.</param>
    /// <param name="result">The validation result collector.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask ValidateAsync(
        AIDataSource dataSource,
        ValidationResultDetails result,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a value indicating whether the documents this handler reads are typed knowledge objects whose
    /// reserved field names address the knowledge base's own typed columns.
    /// </summary>
    /// <remarks>
    /// The knowledge base keeps <c>contentType</c>, <c>rootId</c>, <c>parentId</c> and <c>page</c> as real
    /// columns so a search can filter on them. A handler that reads somebody else's documents may well
    /// surface a field of its own called <c>contentType</c>, and promoting that into the discriminator would
    /// both corrupt the column and make the caller's own filter match nothing. Only a handler that produces
    /// typed knowledge opts in, which is why this defaults to <see langword="false"/>: an existing handler
    /// outside this repository keeps its fields in the per-row bag exactly as before.
    /// </remarks>
    bool ProducesTypedKnowledge => false;

    /// <summary>
    /// Gets the reference type written into the knowledge-base chunks for this source.
    /// </summary>
    /// <param name="dataSource">The AI data source being synchronized.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask<string> GetReferenceTypeAsync(
        AIDataSource dataSource,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads all source documents for a full synchronization.
    /// </summary>
    /// <param name="dataSource">The AI data source being synchronized.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    IAsyncEnumerable<KeyValuePair<string, SourceDocument>> ReadAsync(
        AIDataSource dataSource,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads specific source documents for an incremental synchronization.
    /// </summary>
    /// <param name="dataSource">The AI data source being synchronized.</param>
    /// <param name="documentIds">The source document identifiers.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    IAsyncEnumerable<KeyValuePair<string, SourceDocument>> ReadByIdsAsync(
        AIDataSource dataSource,
        IEnumerable<string> documentIds,
        CancellationToken cancellationToken = default);
}
