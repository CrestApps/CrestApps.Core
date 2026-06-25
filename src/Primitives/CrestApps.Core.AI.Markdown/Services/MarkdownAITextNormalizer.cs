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
    /// <param name="preserveTabular">When <c>true</c>, the tab-delimited layout is preserved instead of being normalized as prose.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<string> NormalizeContentAsync(string text, bool preserveTabular = false, CancellationToken cancellationToken = default)
    {
        if (preserveTabular)
        {
            // Tab-delimited grids must keep their delimiters; the markdown normalizer would collapse them.
            return Task.FromResult(text ?? string.Empty);
        }

        return RagTextNormalizer.NormalizeContentAsync(text, cancellationToken);
    }

    /// <summary>
    /// Normalizes and chunk.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="preserveTabular">When <c>true</c>, the content is chunked by row to preserve the tab-delimited layout.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<List<string>> NormalizeAndChunkAsync(string text, bool preserveTabular = false, CancellationToken cancellationToken = default)
    {
        if (preserveTabular)
        {
            var rows = string.IsNullOrEmpty(text)
                ? []
                : text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();

            return Task.FromResult(rows);
        }

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
