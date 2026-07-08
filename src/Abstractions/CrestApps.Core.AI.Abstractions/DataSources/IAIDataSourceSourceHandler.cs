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
