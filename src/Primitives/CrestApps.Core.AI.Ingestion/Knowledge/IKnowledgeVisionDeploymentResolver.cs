namespace CrestApps.Core.AI.Ingestion.Knowledge;

/// <summary>
/// Says which deployment should transcribe the figures one source produced.
/// </summary>
/// <remarks>
/// Ingestion applies a source's settings directly, but a figure is transcribed afterwards, by the backfill,
/// from a stored object that carries only the source's identifier. Without this the per-source choice would
/// take effect for everything except the one thing it exists to control.
/// <para>
/// The default answers nothing, so a host with no file sources uses its own vision deployment and nothing has to
/// know that file sources exist.
/// </para>
/// </remarks>
public interface IKnowledgeVisionDeploymentResolver
{
    /// <summary>
    /// Resolves the vision deployment configured for one source.
    /// </summary>
    /// <param name="fileSourceId">The source that produced the object, or <see langword="null"/> for a manual upload.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The deployment name, or <see langword="null"/> to use the host's own.</returns>
    Task<string> ResolveAsync(string fileSourceId, CancellationToken cancellationToken = default);
}
