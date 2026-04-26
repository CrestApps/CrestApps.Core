namespace CrestApps.Core.AI.Services;

/// <summary>
/// Normalizes titles and content used by RAG pipelines, and chunks normalized
/// content for embedding/indexing when needed.
/// </summary>
public interface IAITextNormalizer
{
    /// <summary>
    /// Normalize contents content.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<string> NormalizeContentAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Normalize and chunks and chunk.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<List<string>> NormalizeAndChunkAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Normalize titles title.
    /// </summary>
    /// <param name="title">The title.</param>
    string NormalizeTitle(string title);
}
