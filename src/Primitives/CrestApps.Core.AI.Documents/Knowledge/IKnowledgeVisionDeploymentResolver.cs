namespace CrestApps.Core.AI.Documents.Knowledge;

/// <summary>
/// Says which deployment should transcribe the figures one indexer produced.
/// </summary>
/// <remarks>
/// Ingestion applies an indexer's settings directly, but a figure is transcribed afterwards, by the backfill,
/// from a stored object that carries only the indexer's identifier. Without this the per-indexer choice would
/// take effect for everything except the one thing it exists to control.
/// <para>
/// The default answers nothing, so a host with no indexers uses its own vision deployment and nothing has to
/// know that indexers exist.
/// </para>
/// </remarks>
public interface IKnowledgeVisionDeploymentResolver
{
    /// <summary>
    /// Resolves the vision deployment configured for one indexer.
    /// </summary>
    /// <param name="indexerId">The indexer that produced the object, or <see langword="null"/> for a manual upload.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The deployment name, or <see langword="null"/> to use the host's own.</returns>
    Task<string> ResolveAsync(string indexerId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Answers nothing, so the host's vision deployment is used.
/// </summary>
internal sealed class NullKnowledgeVisionDeploymentResolver : IKnowledgeVisionDeploymentResolver
{
    /// <inheritdoc />
    public Task<string> ResolveAsync(string indexerId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string>(null);
    }
}
