using CrestApps.Core.AI.Services;

namespace CrestApps.Core.AI.Markdown.Services;

/// <summary>
/// Markdown-aware implementation of <see cref="IAITextNormalizer"/>.
/// </summary>
public sealed class MarkdownAITextNormalizer : IAITextNormalizer
{
    /// <summary>
    /// Normalize contents content.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<string> NormalizeContentAsync(string text, CancellationToken cancellationToken = default)
    {
        return RagTextNormalizer.NormalizeContentAsync(text, cancellationToken);
    }

    /// <summary>
    /// Normalizes and chunk.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<List<string>> NormalizeAndChunkAsync(string text, CancellationToken cancellationToken = default)
    {
        return RagTextNormalizer.NormalizeAndChunkAsync(text, cancellationToken);
    }

    /// <summary>
    /// Normalizes title.
    /// </summary>
    /// <param name="title">The title.</param>
    public string NormalizeTitle(string title)
    {
        return RagTextNormalizer.NormalizeTitle(title);
    }
}
