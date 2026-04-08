using CrestApps.Core.AI.Services;

namespace CrestApps.Core.AI.Markdown.Services;
/// <summary>
/// Markdown-aware implementation of <see cref = "IAITextNormalizer"/>.
/// </summary>
public sealed class MarkdownAITextNormalizer : IAITextNormalizer
{
    public Task<string> NormalizeContentAsync(string text, CancellationToken cancellationToken = default)
    {
        return RagTextNormalizer.NormalizeContentAsync(text, cancellationToken);
    }

    public Task<List<string>> NormalizeAndChunkAsync(string text, CancellationToken cancellationToken = default)
    {
        return RagTextNormalizer.NormalizeAndChunkAsync(text, cancellationToken);
    }

    public string NormalizeTitle(string title)
    {
        return RagTextNormalizer.NormalizeTitle(title);
    }
}