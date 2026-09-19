using CrestApps.Core.AI.Models;
using CrestApps.Core.Models;

namespace CrestApps.Core.AI.Indexing;

/// <summary>
/// Where content comes from.
/// </summary>
/// <remarks>
/// A connector answers two questions and nothing else: what is there, and give me that one. Everything after
/// it — which reader parses the bytes, what enrichment runs, how the knowledge is stored — is the same
/// whether the item came from a website, a folder, a blob container or an FTP server.
/// <para>
/// A connector is handed an <see cref="IngestionSource"/> rather than any one kind of record, so the same
/// connector reads for a <see cref="CrestApps.Core.AI.Models.FileSource"/> and for a
/// <see cref="CrestApps.Core.AI.Models.WebCrawler"/> without knowing which it was given.
/// </para>
/// <para>
/// Implementations are registered as keyed services under <see cref="Name"/>, which is the configured
/// record's source.
/// </para>
/// </remarks>
public interface IIngestionConnector
{
    /// <summary>
    /// Gets the connector's registered name, which is stored as the configured record's source.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Validates a configured record's settings.
    /// </summary>
    /// <param name="source">The configured record.</param>
    /// <param name="result">The validation result to add failures to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask ValidateAsync(IngestionSource source, ValidationResultDetails result, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists what the source holds.
    /// </summary>
    /// <param name="source">The configured record.</param>
    /// <param name="continuationToken">
    /// Where to resume, from the previous run's <see cref="IngestionDiscoveryResult.DiscoveryCursor"/>, or
    /// <see langword="null"/> to start from the beginning. A connector that cannot page ignores it.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What was found, and whether that is all of it.</returns>
    /// <remarks>
    /// A connector that could not list everything must say so rather than returning what it managed. A
    /// partial listing taken for a complete one deletes everything it failed to see.
    /// </remarks>
    Task<IngestionDiscoveryResult> DiscoverAsync(IngestionSource source, string continuationToken = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches one item.
    /// </summary>
    /// <param name="source">The configured record.</param>
    /// <param name="itemId">The item to fetch.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The content, or <see langword="null"/> when the item is gone or empty.</returns>
    Task<IngestionItemContent> FetchAsync(IngestionSource source, string itemId, CancellationToken cancellationToken = default);
}
