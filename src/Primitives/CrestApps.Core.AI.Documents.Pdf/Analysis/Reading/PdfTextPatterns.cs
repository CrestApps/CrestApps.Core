using System.Text;
using System.Text.RegularExpressions;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Recognises the kinds of line a page's layout gives away by their wording: list items, captions and page
/// numbers, and writes text the compact way the reading tools report it.
/// </summary>
internal static partial class PdfTextPatterns
{
    /// <summary>
    /// Returns whether a line starts the way a list item does: a bullet, or a number or letter followed by
    /// a full stop or a closing parenthesis.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <returns><see langword="true"/> for lines such as <c>• North</c>, <c>2. South</c> or <c>b) East</c>.</returns>
    public static bool IsListItem(string line)
    {
        return !string.IsNullOrWhiteSpace(line) && ListItemRegex().IsMatch(line);
    }

    /// <summary>
    /// Returns whether a line starts with a bullet glyph rather than a number.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <returns><see langword="true"/> for lines such as <c>• North</c>.</returns>
    public static bool IsBulleted(string line)
    {
        return !string.IsNullOrWhiteSpace(line) && BulletRegex().IsMatch(line);
    }

    /// <summary>
    /// Splits a list item into its marker and its text.
    /// </summary>
    /// <param name="line">The list item.</param>
    /// <param name="marker">The marker, such as <c>•</c> or <c>2.</c>, or an empty string.</param>
    /// <returns>The text after the marker.</returns>
    public static string StripListMarker(string line, out string marker)
    {
        marker = string.Empty;

        if (string.IsNullOrWhiteSpace(line))
        {
            return line ?? string.Empty;
        }

        var match = ListMarkerRegex().Match(line);

        if (!match.Success)
        {
            return line.Trim();
        }

        marker = match.Groups["marker"].Value;

        return line[match.Length..].Trim();
    }

    /// <summary>
    /// Returns whether a line reads as a caption: <c>Figure 1</c>, <c>Fig. 2</c>, <c>Table 3</c>,
    /// <c>Chart 4</c> and the like.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when the text starts as a caption does.</returns>
    public static bool IsCaption(string text)
    {
        return !string.IsNullOrWhiteSpace(text) && text.Length <= 400 && CaptionRegex().IsMatch(text.TrimStart());
    }

    /// <summary>
    /// Returns whether a line holds nothing but a page number, such as <c>12</c>, <c>- 3 -</c> or
    /// <c>Page 3 of 10</c>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> for a bare page number.</returns>
    public static bool IsPageNumber(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 40)
        {
            return false;
        }

        var trimmed = text.Trim();

        return PageNumberRegex().IsMatch(trimmed) || RomanNumeralRegex().IsMatch(trimmed);
    }

    /// <summary>
    /// Reduces text to the form repeated running heads share across pages: lower case, digits folded to
    /// <c>#</c> and whitespace collapsed, so "Page 3" and "Page 4" read alike.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The signature.</returns>
    public static string Signature(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;

                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.IsDigit(character) ? '#' : char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Collapses every run of whitespace, line breaks included, to a single space.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The text on one line.</returns>
    public static string OneLine(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;

                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Shortens text to a number of characters, marking the cut.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="maxCharacters">The most characters kept; zero or less keeps everything.</param>
    /// <returns>The text, ending in an ellipsis when it was cut.</returns>
    public static string Truncate(string text, int maxCharacters)
    {
        if (string.IsNullOrEmpty(text) || maxCharacters <= 0 || text.Length <= maxCharacters)
        {
            return text ?? string.Empty;
        }

        var cut = text.LastIndexOf(' ', Math.Max(0, maxCharacters - 1));

        if (cut < maxCharacters * 2 / 3)
        {
            cut = maxCharacters;
        }

        return text[..cut].TrimEnd() + "…";
    }

    /// <summary>
    /// Counts the letters in text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The number of letters.</returns>
    public static int CountLetters(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var count = 0;

        foreach (var character in text)
        {
            if (char.IsLetter(character))
            {
                count++;
            }
        }

        return count;
    }

    [GeneratedRegex(@"^\s*(?:[•◦▪▫‣⁃●○■□►▸▹▶➢➤✓✔✗∙·*+–—-]|\(?\d{1,3}[.)]|\(?[a-z][.)]|[A-Z]\)|\(?(?:i|ii|iii|iv|v|vi|vii|viii|ix|x)[.)])\s+\S", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex ListItemRegex();

    [GeneratedRegex(@"^\s*[•◦▪▫‣⁃●○■□►▸▹▶➢➤✓✔✗∙·*+–—-]\s+\S", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"^\s*(?<marker>[•◦▪▫‣⁃●○■□►▸▹▶➢➤✓✔✗∙·*+–—-]|\(?\d{1,3}[.)]|\(?[a-z][.)]|[A-Z]\)|\(?(?:i|ii|iii|iv|v|vi|vii|viii|ix|x)[.)])\s+", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex ListMarkerRegex();

    [GeneratedRegex(@"^(?:figure|fig\.|table|tab\.|chart|graph|exhibit|diagram|illustration|plate|map|photo)\s*(?:\d+(?:[.\-]\d+)*|[ivxlc]+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex CaptionRegex();

    [GeneratedRegex(@"^(?:page|p\.|pg\.?|seite|página|pagina)?\s*[\-–—(\[]?\s*\d{1,4}\s*[\-–—)\]]?\s*(?:(?:of|/|von|de|di)\s*\d{1,4})?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex PageNumberRegex();

    [GeneratedRegex(@"^(?=[mdclxvi])m{0,3}(?:cm|cd|d?c{0,3})(?:xc|xl|l?x{0,3})(?:ix|iv|v?i{0,3})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex RomanNumeralRegex();
}
