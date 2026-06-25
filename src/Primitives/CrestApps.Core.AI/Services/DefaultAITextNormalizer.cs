using System.Net;
using System.Text.RegularExpressions;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Provides a framework-default text normalizer that works without any
/// Markdown-specific dependencies.
/// </summary>
public sealed partial class DefaultAITextNormalizer : IAITextNormalizer
{
    private const int MaxChunkLength = 4000;
    private const int ChunkOverlapLength = 200;
    private const int MinBoundarySearchLength = 2000;

    /// <summary>
    /// Normalize contents content.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="preserveTabular">When <c>true</c>, the tab-delimited layout is preserved instead of being normalized as prose.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<string> NormalizeContentAsync(string text, bool preserveTabular = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (preserveTabular || string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(text);
        }

        text = StripHtml(text);
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        text = HorizontalSpacesRegex().Replace(text, " ");
        text = MultipleNewlinesRegex().Replace(text, "\n\n");

        return Task.FromResult(text.Trim());
    }

    /// <summary>
    /// Normalizes and chunk.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="preserveTabular">When <c>true</c>, the content is chunked by row to preserve the tab-delimited layout.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<List<string>> NormalizeAndChunkAsync(string text, bool preserveTabular = false, CancellationToken cancellationToken = default)
    {
        if (preserveTabular)
        {
            // Tab-delimited grids are chunked by row so each row keeps its columns intact.
            return string.IsNullOrEmpty(text)
                ? []
                : text.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        var normalized = await NormalizeContentAsync(text, preserveTabular, cancellationToken);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [];
        }

        return ChunkText(normalized, cancellationToken);
    }

    /// <summary>
    /// Normalizes title.
    /// </summary>
    /// <param name="title">The title.</param>
    public string NormalizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        title = StripHtml(title);
        title = AllWhitespaceRegex().Replace(title, " ");

        return title.Trim();
    }

    private static List<string> ChunkText(string text, CancellationToken cancellationToken)
    {
        var chunks = new List<string>();
        var start = 0;

        while (start < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining = text.Length - start;
            var length = Math.Min(MaxChunkLength, remaining);
            var end = start + length;

            if (end < text.Length)
            {
                end = FindChunkBoundary(text, start, end);
            }

            var chunk = text.Substring(start, end - start).Trim();

            if (!string.IsNullOrWhiteSpace(chunk))
            {
                chunks.Add(chunk);
            }

            if (end >= text.Length)
            {
                break;
            }

            start = Math.Max(end - ChunkOverlapLength, start + 1);

            while (start < text.Length && char.IsWhiteSpace(text[start]))
            {
                start++;
            }
        }

        return chunks;
    }

    private static int FindChunkBoundary(string text, int start, int end)
    {
        var searchStart = Math.Max(start + MinBoundarySearchLength, start);
        var paragraphBoundary = text.LastIndexOf("\n\n", end - 1, end - searchStart, StringComparison.Ordinal);

        if (paragraphBoundary >= searchStart)
        {
            return paragraphBoundary;
        }

        for (var i = end - 1; i >= searchStart; i--)
        {
            if (text[i] == '.' || text[i] == '!' || text[i] == '?')
            {
                return i + 1;
            }
        }

        for (var i = end - 1; i >= searchStart; i--)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                return i;
            }
        }

        return end;
    }

    private static string StripHtml(string text)
    {
        text = BrTagRegex().Replace(text, "\n");
        text = BlockCloseTagRegex().Replace(text, "\n");
        text = HtmlTagRegex().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = text.Replace("\u00B6", string.Empty, StringComparison.Ordinal);

        return text;
    }

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BrTagRegex();

    [GeneratedRegex(@"</(p|div|h[1-6]|li|tr|blockquote|pre|section|article|header|footer|nav|main|aside)>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockCloseTagRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"[^\S\n]+")]
    private static partial Regex HorizontalSpacesRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultipleNewlinesRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex AllWhitespaceRegex();
}
