namespace CrestApps.Core.AI.Indexing;

/// <summary>
/// Finds the connector a configured indexer names.
/// </summary>
/// <remarks>
/// Connectors are registered as keyed services under their name, and keyed services cannot be enumerated or
/// probed without a provider. This is the one place that knows how to turn the source string stored on an
/// indexer record back into the connector that serves it, so the run service, the catalog handler and the
/// admin screens all agree on which records are indexers.
/// </remarks>
public interface IIngestionConnectorResolver
{
    /// <summary>
    /// Resolves the connector registered under the supplied name.
    /// </summary>
    /// <param name="name">The connector name, which is the indexer's source.</param>
    /// <returns>The connector, or <see langword="null"/> when none is registered.</returns>
    IIngestionConnector Get(string name);
}
