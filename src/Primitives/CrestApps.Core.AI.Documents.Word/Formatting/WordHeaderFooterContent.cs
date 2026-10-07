using System.Globalization;
using System.Text.RegularExpressions;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Fields;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Formatting;

/// <summary>
/// Writes the text of headers and footers: literal text with inline Markdown, and tokens such as
/// <c>{page}</c> and <c>{pages}</c> that become the fields Word fills in on every page.
/// </summary>
internal static partial class WordHeaderFooterContent
{
    /// <summary>
    /// The tokens text may contain, described for tool schemas.
    /// </summary>
    public const string Tokens = "{page}, {pages}, {section_pages}, {date}, {title}, {author}, {file_name}";

    /// <summary>
    /// Builds a paragraph with text at the left, center and right of the line.
    /// </summary>
    /// <param name="left">The left text, or <see langword="null"/>.</param>
    /// <param name="center">The centered text, or <see langword="null"/>.</param>
    /// <param name="right">The right text, or <see langword="null"/>.</param>
    /// <param name="widthTwips">The text width, where the right tab stop goes.</param>
    /// <param name="styleId">The paragraph style, such as Header or Footer.</param>
    /// <param name="context">What the tokens resolve against.</param>
    /// <returns>The paragraph.</returns>
    public static Paragraph Zones(string left, string center, string right, int widthTwips, string styleId, WordHeaderFooterTokens context)
    {
        var paragraph = new Paragraph(new ParagraphProperties
        {
            ParagraphStyleId = new ParagraphStyleId { Val = styleId },
            Tabs = new Tabs(
                new TabStop { Val = TabStopValues.Center, Position = widthTwips / 2 },
                new TabStop { Val = TabStopValues.Right, Position = widthTwips }),
        });

        Append(paragraph, left, context);

        if (!string.IsNullOrEmpty(center) || !string.IsNullOrEmpty(right))
        {
            paragraph.Append(new Run(new TabChar()));
            Append(paragraph, center, context);
        }

        if (!string.IsNullOrEmpty(right))
        {
            paragraph.Append(new Run(new TabChar()));
            Append(paragraph, right, context);
        }

        return paragraph;
    }

    /// <summary>
    /// Appends text with tokens to a paragraph.
    /// </summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <param name="text">The text.</param>
    /// <param name="context">What the tokens resolve against.</param>
    public static void Append(Paragraph paragraph, string text, WordHeaderFooterTokens context)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var position = 0;

        foreach (Match match in TokenPattern().Matches(text))
        {
            WordInlineWriter.AppendMarkdown(paragraph, text[position..match.Index], context.Part, context.Format);

            var (instruction, result) = match.Groups[1].Value.ToLowerInvariant() switch
            {
                "page" => ("PAGE", "1"),
                "pages" => ("NUMPAGES", "1"),
                "section_pages" => ("SECTIONPAGES", "1"),
                "date" => ("DATE \\@ \"MMMM d, yyyy\"", context.Today.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)),
                "title" => ("TITLE", context.Title ?? string.Empty),
                "author" => ("AUTHOR", context.Author ?? string.Empty),
                _ => ("FILENAME", context.FileName ?? string.Empty),
            };

            WordFieldWriter.Append(paragraph, instruction, result, context.Format);
            position = match.Index + match.Length;
        }

        WordInlineWriter.AppendMarkdown(paragraph, text[position..], context.Part, context.Format);
    }

    /// <summary>
    /// Returns whether text holds a page number token.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when the text shows a page number.</returns>
    public static bool HasPageNumber(string text)
    {
        return text is not null && text.Contains("{page}", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"\{(page|pages|section_pages|date|title|author|file_name)\}", RegexOptions.IgnoreCase)]
    private static partial Regex TokenPattern();
}

/// <summary>
/// What the tokens of a header or footer resolve against.
/// </summary>
/// <param name="Part">The header or footer part, which owns any link.</param>
/// <param name="Format">The run formatting of the text, or <see langword="null"/>.</param>
/// <param name="Title">The document title.</param>
/// <param name="Author">The document author.</param>
/// <param name="FileName">The document's file name.</param>
/// <param name="Today">The date shown until Word updates the field.</param>
internal sealed record WordHeaderFooterTokens(OpenXmlPart Part, WordRunFormat Format, string Title, string Author, string FileName, DateTime Today);
