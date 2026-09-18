namespace CrestApps.Core.AI.Ingestion.Processors;

/// <summary>
/// Remembers what a figure was transcribed as, so the same artwork is never described twice.
/// </summary>
/// <remarks>
/// The prompt version is part of the key on purpose. A transcription produced by an older prompt is not the
/// transcription the current prompt would produce, and serving it would hide a prompt change behind a cache.
/// </remarks>
public interface IFigureDescriptionCache
{
    /// <summary>
    /// Looks up a stored description.
    /// </summary>
    /// <param name="contentHash">The hash of the figure bytes.</param>
    /// <param name="promptVersion">The version of the transcription prompt.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The description, or <see langword="null"/> when nothing is stored for it.</returns>
    Task<string> TryGetAsync(string contentHash, string promptVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a description.
    /// </summary>
    /// <param name="contentHash">The hash of the figure bytes.</param>
    /// <param name="promptVersion">The version of the transcription prompt.</param>
    /// <param name="description">The description.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task SetAsync(string contentHash, string promptVersion, string description, CancellationToken cancellationToken = default);
}
