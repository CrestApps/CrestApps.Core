using CrestApps.Core.AI.Models;
using CrestApps.Core.Models;

namespace CrestApps.Core.AI.Indexing;

/// <summary>
/// One item a connector found, and enough about it to tell whether it has changed.
/// </summary>
/// <param name="ItemId">What the connector knows the item by. Stable across runs.</param>
/// <param name="ChangeToken">An opaque value that changes when the item does. Compared verbatim, never parsed.</param>
/// <param name="SizeBytes">How large the item is, when the connector knows.</param>
/// <param name="LastModifiedUtc">When the item last changed, when the connector knows.</param>
public sealed record IngestionItemRef(
    string ItemId,
    string ChangeToken = null,
    long? SizeBytes = null,
    DateTimeOffset? LastModifiedUtc = null);

/// <summary>
/// What one discovery pass found, and whether it found everything.
/// </summary>
/// <param name="Items">The items.</param>
/// <param name="IsComplete">
/// Whether the listing is the whole of what the source holds. A partial listing must never be used to decide
/// that anything was deleted.
/// </param>
/// <param name="Message">Why the listing was incomplete, when it was.</param>
/// <param name="DiscoveryCursor">
/// Where the next run should resume, when this listing was only a window onto the source. A result that
/// carries one is never complete, so nothing is ever removed on the strength of a window.
/// </param>
public sealed record IngestionDiscoveryResult(
    IReadOnlyList<IngestionItemRef> Items,
    bool IsComplete,
    string Message = null,
    string DiscoveryCursor = null);

/// <summary>
/// One fetched item, ready to be read.
/// </summary>
public sealed class IngestionItemContent : IAsyncDisposable
{
    /// <summary>
    /// Gets the content.
    /// </summary>
    public Stream Content { get; init; }

    /// <summary>
    /// Gets the media type the source declared, when it declared one.
    /// </summary>
    public string MediaType { get; init; }

    /// <summary>
    /// Gets the item's title, when the source has one.
    /// </summary>
    public string Title { get; init; }

    /// <summary>
    /// Gets the file name, which decides the reader when the media type says nothing.
    /// </summary>
    public string FileName { get; init; }

    /// <summary>
    /// Releases the content.
    /// </summary>
    /// <returns>A task that completes when the content is released.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Content is not null)
        {
            await Content.DisposeAsync();
        }
    }
}

/// <summary>
/// Where content comes from.
/// </summary>
/// <remarks>
/// A connector answers two questions and nothing else: what is there, and give me that one. Everything after
/// it — which reader parses the bytes, what enrichment runs, how the knowledge is stored — is the same
/// whether the item came from a website, a folder, a blob container or an FTP server.
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
    /// <param name="settings">The configured record.</param>
    /// <param name="result">The validation result to add failures to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask ValidateAsync(WebCrawler settings, ValidationResultDetails result, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists what the source holds.
    /// </summary>
    /// <param name="settings">The configured record.</param>
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
    Task<IngestionDiscoveryResult> DiscoverAsync(WebCrawler settings, string continuationToken = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches one item.
    /// </summary>
    /// <param name="settings">The configured record.</param>
    /// <param name="itemId">The item to fetch.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The content, or <see langword="null"/> when the item is gone or empty.</returns>
    Task<IngestionItemContent> FetchAsync(WebCrawler settings, string itemId, CancellationToken cancellationToken = default);
}
