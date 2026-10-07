using System.Text;
using System.Text.RegularExpressions;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using UglyToad.PdfPig.Content;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Finds text on a page and reports where it is drawn.
/// </summary>
/// <remarks>
/// A page is searched as a reader sees it — words joined by single spaces, lines by newlines, in reading
/// order — so a phrase that wraps, or that the file drew with no space glyphs, is still found. Every
/// character of that text remembers the glyph it came from, which is how a match becomes the boxes a
/// highlight, a link or a redaction is placed over.
/// </remarks>
internal static class PdfTextFinder
{
    private static readonly TimeSpan _regexTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Finds text on a page.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="query">The text, or the regular expression, to find.</param>
    /// <param name="isRegex">Whether <paramref name="query"/> is a regular expression.</param>
    /// <param name="matchCase">Whether case must match.</param>
    /// <param name="wholeWord">Whether a match must start and end on word boundaries.</param>
    /// <param name="maxMatches">The most matches returned.</param>
    /// <returns>The matches, in reading order.</returns>
    public static List<PdfTextMatch> Find(Page page, string query, bool isRegex, bool matchCase, bool wholeWord, int maxMatches)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (string.IsNullOrEmpty(query) || maxMatches <= 0)
        {
            return [];
        }

        var (text, letters) = BuildIndex(page);

        if (text.Length == 0)
        {
            return [];
        }

        var pattern = isRegex
            ? query
            : Regex.Escape(query.Trim()).Replace("\\ ", "\\s+", StringComparison.Ordinal);

        if (wholeWord)
        {
            pattern = "(?<![\\p{L}\\p{N}])(?:" + pattern + ")(?![\\p{L}\\p{N}])";
        }

        Regex regex;

        try
        {
            regex = new Regex(
                pattern,
                (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.CultureInvariant,
                _regexTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new PdfToolException($"\"{query}\" is not a valid regular expression: {ex.Message}", ex);
        }

        var matches = new List<PdfTextMatch>();

        try
        {
            foreach (Match match in regex.Matches(text))
            {
                if (match.Length == 0)
                {
                    continue;
                }

                var boxes = BoxesFor(letters, match.Index, match.Length);

                if (boxes.Count == 0)
                {
                    continue;
                }

                matches.Add(new PdfTextMatch(page.Number, match.Value, Snippet(text, match.Index, match.Length), boxes)
                {
                    Letters = LettersFor(letters, match.Index, match.Length),
                });

                if (matches.Count >= maxMatches)
                {
                    break;
                }
            }
        }
        catch (RegexMatchTimeoutException ex)
        {
            throw new PdfToolException($"The pattern \"{query}\" took too long to evaluate; simplify it.", ex);
        }

        return matches;
    }

    /// <summary>
    /// Finds the values <see cref="PdfPatternLibrary"/> recognises on a page — email addresses, card numbers,
    /// identity numbers and the rest — with where each is drawn.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="kinds">The kinds to look for, or <see langword="null"/> for all.</param>
    /// <returns>The matches, in reading order, each carrying its <see cref="PdfTextMatch.Kind"/>.</returns>
    public static List<PdfTextMatch> FindPatterns(Page page, IEnumerable<string> kinds)
    {
        ArgumentNullException.ThrowIfNull(page);

        var (text, letters) = BuildIndex(page);
        var matches = new List<PdfTextMatch>();

        foreach (var found in PdfPatternLibrary.Find(text, kinds))
        {
            var boxes = BoxesFor(letters, found.Index, found.Length);

            if (boxes.Count == 0)
            {
                continue;
            }

            matches.Add(new PdfTextMatch(page.Number, found.Value, Snippet(text, found.Index, found.Length), boxes)
            {
                Letters = LettersFor(letters, found.Index, found.Length),
                Kind = found.Kind,
            });
        }

        return matches;
    }

    /// <summary>
    /// Builds the page's searchable text and, for every character of it, the glyph it came from.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>The text and a parallel list of glyphs; <see langword="null"/> marks a space or line break the reader sees but the file does not draw.</returns>
    public static (string Text, List<Letter> Letters) BuildIndex(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var builder = new StringBuilder();
        var letters = new List<Letter>();

        foreach (var block in PdfPageText.GetBlocks(page))
        {
            foreach (var line in block.TextLines)
            {
                var firstWord = true;

                foreach (var word in line.Words)
                {
                    if (!firstWord)
                    {
                        builder.Append(' ');
                        letters.Add(null);
                    }

                    firstWord = false;

                    foreach (var letter in word.Letters)
                    {
                        foreach (var character in letter.Value ?? string.Empty)
                        {
                            builder.Append(character);
                            letters.Add(letter);
                        }
                    }
                }

                builder.Append('\n');
                letters.Add(null);
            }
        }

        return (builder.ToString(), letters);
    }

    private static List<PdfBox> BoxesFor(List<Letter> letters, int start, int length)
    {
        var boxes = new List<PdfBox>();
        PdfBox? current = null;
        Letter previous = null;

        for (var index = start; index < start + length && index < letters.Count; index++)
        {
            var letter = letters[index];

            if (letter is null || ReferenceEquals(letter, previous))
            {
                continue;
            }

            var box = PdfBox.From(letter.BoundingBox);

            // A space glyph has no ink and a zero-width box; it would stretch nothing and is skipped.
            if (box.Width <= 0.01 || box.Height <= 0.01)
            {
                previous = letter;

                continue;
            }

            if (current is { } open && previous is not null && SameLine(previous, letter))
            {
                current = open.Union(box);
            }
            else
            {
                if (current.HasValue)
                {
                    boxes.Add(current.Value);
                }

                current = box;
            }

            previous = letter;
        }

        if (current.HasValue)
        {
            boxes.Add(current.Value);
        }

        return boxes;
    }

    private static List<Letter> LettersFor(List<Letter> letters, int start, int length)
    {
        var result = new List<Letter>();

        for (var index = start; index < start + length && index < letters.Count; index++)
        {
            if (letters[index] is { } letter && (result.Count == 0 || !ReferenceEquals(result[^1], letter)))
            {
                result.Add(letter);
            }
        }

        return result;
    }

    private static bool SameLine(Letter previous, Letter next)
    {
        return Math.Abs(previous.StartBaseLine.Y - next.StartBaseLine.Y) < Math.Max(1, previous.PointSize * 0.3) &&
            next.StartBaseLine.X >= previous.StartBaseLine.X - 1;
    }

    private static string Snippet(string text, int start, int length)
    {
        const int Context = 60;

        var from = Math.Max(0, start - Context);
        var to = Math.Min(text.Length, start + length + Context);
        var snippet = text[from..to].Replace('\n', ' ');

        return (from > 0 ? "…" : string.Empty) + snippet.Trim() + (to < text.Length ? "…" : string.Empty);
    }
}
