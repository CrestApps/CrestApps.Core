using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.FileSources.FileTransfer;

/// <summary>
/// Creates the client one file source's settings describe.
/// </summary>
public interface IRemoteFileClientFactory
{
    /// <summary>
    /// Gets the connector name the factory serves.
    /// </summary>
    string ConnectorName { get; }

    /// <summary>
    /// Creates a client for the supplied source.
    /// </summary>
    /// <param name="source">The configured source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The client.</returns>
    Task<IRemoteFileClient> CreateAsync(IngestionSource source, CancellationToken cancellationToken = default);
}
