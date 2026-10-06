using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Reading;

/// <summary>
/// Reads the text an element shows: what a reader sees on the page, with tabs and line breaks, without the
/// codes of fields or text a tracked change deleted.
/// </summary>
internal static class WordText
{
    /// <summary>
    /// Reads the visible text of an element.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="includeDeleted">Whether text removed by a tracked change is included.</param>
    /// <returns>The text.</returns>
    public static string Of(OpenXmlElement element, bool includeDeleted = false)
    {
        if (element is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        Append(element, builder, includeDeleted, paragraphSeparator: element is Paragraph ? null : "\n");

        return builder.ToString().Trim('\n');
    }

    /// <summary>
    /// Reads the visible text of a table cell, its paragraphs joined by line breaks.
    /// </summary>
    /// <param name="cell">The cell.</param>
    /// <returns>The text.</returns>
    public static string OfCell(TableCell cell)
    {
        return cell is null
            ? string.Empty
            : string.Join("\n", cell.Elements<Paragraph>().Select(paragraph => Of(paragraph))).Trim();
    }

    /// <summary>
    /// Shortens text for a listing, on a word boundary, saying it was cut.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="length">The most characters.</param>
    /// <returns>The text, shortened when longer than <paramref name="length"/>.</returns>
    public static string Clip(string text, int length)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var flat = text.Replace('\n', ' ').Replace('\t', ' ');

        if (flat.Length <= length)
        {
            return flat;
        }

        var cut = flat.LastIndexOf(' ', Math.Max(0, length - 1));

        if (cut < length / 2)
        {
            cut = length;
        }

        return flat[..cut].TrimEnd() + "…";
    }

    /// <summary>
    /// Counts the words in a text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The number of words.</returns>
    public static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var count = 0;
        var inWord = false;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                count++;
            }
        }

        return count;
    }

    private static void Append(OpenXmlElement element, StringBuilder builder, bool includeDeleted, string paragraphSeparator)
    {
        switch (element)
        {
            case Text text:
                builder.Append(text.Text);

                return;

            case DeletedText deleted:
                if (includeDeleted)
                {
                    builder.Append(deleted.Text);
                }

                return;

            case FieldCode:
            case DeletedFieldCode:
                return;

            case TabChar:
            case PositionalTab:
                builder.Append('\t');

                return;

            case Break:
            case CarriageReturn:
                builder.Append('\n');

                return;

            case NoBreakHyphen:
                builder.Append('-');

                return;

            case SoftHyphen:
                return;

            case SymbolChar symbol when symbol.Char?.Value is { Length: 4 } code && int.TryParse(code, System.Globalization.NumberStyles.HexNumber, null, out var value):
                // A symbol font character is mapped from the private-use area it is stored in.
                builder.Append(value >= 0xF000 ? (char)(value - 0xF000) : (char)value);

                return;

            case DocumentFormat.OpenXml.Wordprocessing.Drawing:
            case Picture:
                return;
        }

        foreach (var child in element.ChildElements)
        {
            Append(child, builder, includeDeleted, paragraphSeparator);
        }

        if (element is Paragraph && paragraphSeparator is not null)
        {
            builder.Append(paragraphSeparator);
        }
    }
}
