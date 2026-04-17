using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.ML.Tokenizers;

namespace CrestApps.Core.AI.Services;
/// <summary>
/// Provides text normalization and chunking utilities for RAG (Retrieval-Augmented Generation) content.
/// Strips HTML tags, uses <see cref = "MarkdownReader"/> for Markdown-to-plain-text conversion,
/// and provides token-aware chunking via <see cref = "DocumentTokenChunker"/>.
/// </summary>
public static partial class RagTextNormalizer
{
    private const int FallbackMaxChunkLength = 4000;
    private const int FallbackChunkOverlapLength = 200;
    private const int FallbackMinBoundarySearchLength = 2000;
    private static readonly MarkdownReader _reader = CreateMarkdownReader();
    private static readonly DocumentTokenChunker _defaultChunker = CreateDefaultChunker();

    public static async Task<string> NormalizeContentAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var strippedText = StripHtml(text);

        try
        {
            var document = await ParseDocumentAsync(strippedText, cancellationToken);
            var normalized = JoinDocumentText(document);

            return NormalizeContentWhitespace(normalized).Trim();
        }
        catch (NotSupportedException ex) when (IsUnsupportedMarkdownInline(ex))
        {
            cancellationToken.ThrowIfCancellationRequested();

            return FallbackNormalizeContent(strippedText);
        }
    }

    public static async Task<List<string>> NormalizeAndChunkAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var strippedText = StripHtml(text);

        try
        {
            var document = await ParseDocumentAsync(strippedText, cancellationToken);
            var chunks = new List<string>();
            await foreach (var chunk in _defaultChunker.ProcessAsync(document, cancellationToken))
            {
                if (!string.IsNullOrWhiteSpace(chunk.Content))
                {
                    chunks.Add(chunk.Content);
                }
            }

            return chunks;
        }
        catch (NotSupportedException ex) when (IsUnsupportedMarkdownInline(ex))
        {
            cancellationToken.ThrowIfCancellationRequested();

            return FallbackChunkText(FallbackNormalizeContent(strippedText), cancellationToken);
        }
    }

    public static string NormalizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        title = StripHtml(title);
        title = AllWhitespaceRegex().Replace(title, " ");
        return title.Trim();
    }

    internal static string StripHtml(string text)
    {
        text = BrTagRegex().Replace(text, "\n");
        text = BlockCloseTagRegex().Replace(text, "\n");
        text = HtmlTagRegex().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = text.Replace("\u00B6", string.Empty);
        return text;
    }

    internal static MarkdownReader CreateMarkdownReader() => new();
    internal static DocumentTokenChunker CreateDefaultChunker()
    {
        return new(new IngestionChunkerOptions(TiktokenTokenizer.CreateForModel("gpt-4o")) { MaxTokensPerChunk = 500, OverlapTokens = 50, });
    }

    private static async Task<IngestionDocument> ParseDocumentAsync(string text, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return await _reader.ReadAsync(stream, "inmemory", "text/markdown", cancellationToken);
    }

    private static string JoinDocumentText(IngestionDocument document)
    {
        return string.Join("\n", document.EnumerateContent().Select(e => e.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
    }

    private static string NormalizeContentWhitespace(string text)
    {
        text = HorizontalSpacesRegex().Replace(text, " ");
        text = MultipleNewlinesRegex().Replace(text, "\n\n");

        return text;
    }

    private static string FallbackNormalizeContent(string text)
    {
        return NormalizeContentWhitespace(text).Trim();
    }

    private static bool IsUnsupportedMarkdownInline(NotSupportedException ex)
    {
        return ex.Message.Contains("Inline type", StringComparison.Ordinal)
            && ex.Message.Contains("is not supported", StringComparison.Ordinal);
    }

    private static List<string> FallbackChunkText(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var chunks = new List<string>();
        var start = 0;

        while (start < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining = text.Length - start;
            var length = Math.Min(FallbackMaxChunkLength, remaining);
            var end = start + length;

            if (end < text.Length)
            {
                end = FindFallbackChunkBoundary(text, start, end);
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

            start = Math.Max(end - FallbackChunkOverlapLength, start + 1);

            while (start < text.Length && char.IsWhiteSpace(text[start]))
            {
                start++;
            }
        }

        return chunks;
    }

    private static int FindFallbackChunkBoundary(string text, int start, int end)
    {
        var searchStart = Math.Max(start + FallbackMinBoundarySearchLength, start);
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
