using System.Globalization;
using System.Security.Cryptography;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.OpenXml.Word;

/// <summary>
/// Gives every paragraph and table row of a document the stable identifier Word itself uses
/// (<c>w14:paraId</c>), so a tool call can name an element and still find the same element after other
/// content was added or removed before it.
/// </summary>
/// <remarks>
/// A position such as "the fifth paragraph" moves whenever anything is inserted above it, so an edit asked
/// for two turns later would land on the wrong text. The paragraph identifier travels with the paragraph. A
/// table has no identifier of its own and is named by its first row's. Values stay below <c>0x80000000</c>, as
/// the format requires, and unique across the main document, its headers, footers, footnotes and comments.
/// </remarks>
internal sealed class WordParagraphIds
{
    private const int MaxValue = 0x7FFFFFFF;

    private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);

    private WordParagraphIds()
    {
    }

    /// <summary>
    /// Reads the identifiers a document uses and gives every paragraph and row of its body one, replacing any
    /// that is missing, malformed or used twice.
    /// </summary>
    /// <param name="mainPart">The main document part.</param>
    /// <returns>The identifier registry.</returns>
    public static WordParagraphIds Ensure(MainDocumentPart mainPart)
    {
        ArgumentNullException.ThrowIfNull(mainPart);

        var ids = new WordParagraphIds();
        var pending = new List<OpenXmlElement>();

        foreach (var root in EnumerateRoots(mainPart))
        {
            foreach (var element in root.Descendants().Where(element => element is Paragraph or TableRow))
            {
                var value = Read(element);

                if (IsValid(value) && ids._used.Add(value))
                {
                    continue;
                }

                pending.Add(element);
            }
        }

        // A document opened without identifiers gets the same ones every time it is read, so an id a tool reported
        // from an upload still names the same element when a later edit opens it again.
        var candidate = 0u;

        foreach (var element in pending)
        {
            string value;

            do
            {
                value = ((int)((++candidate * 2_654_435_761u) % (MaxValue - 1)) + 1).ToString("X8", CultureInfo.InvariantCulture);
            }
            while (!ids._used.Add(value));

            Write(element, value);
        }

        return ids;
    }

    /// <summary>
    /// Returns a new identifier no element of the document uses.
    /// </summary>
    /// <returns>Eight uppercase hexadecimal digits.</returns>
    public string Next()
    {
        while (true)
        {
            var value = RandomNumberGenerator.GetInt32(1, MaxValue).ToString("X8", CultureInfo.InvariantCulture);

            if (_used.Add(value))
            {
                return value;
            }
        }
    }

    /// <summary>
    /// Gives every paragraph and row of a new element tree an identifier of its own, including the ones it
    /// was copied with.
    /// </summary>
    /// <param name="element">The element that is about to be inserted.</param>
    public void Assign(OpenXmlElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (element is Paragraph or TableRow)
        {
            Write(element, Next());
        }

        foreach (var descendant in element.Descendants().Where(descendant => descendant is Paragraph or TableRow))
        {
            Write(descendant, Next());
        }
    }

    /// <summary>
    /// Returns the identifier a block is named by: a paragraph's own, a table's first row's, or the first
    /// paragraph's of any other container such as a content control.
    /// </summary>
    /// <param name="element">The block.</param>
    /// <returns>The identifier, or <see langword="null"/> when the block holds no paragraph or row.</returns>
    public static string Of(OpenXmlElement element)
    {
        return element switch
        {
            null => null,
            Paragraph paragraph => Read(paragraph),
            TableRow row => Read(row),
            Table table => Read(table.Elements<TableRow>().FirstOrDefault()),
            _ => Read(element.Descendants().FirstOrDefault(descendant => descendant is Paragraph or TableRow)),
        };
    }

    /// <summary>
    /// Normalizes an identifier a model passed: surrounding quotes, a leading <c>#</c> and case are ignored.
    /// </summary>
    /// <param name="value">The identifier as written.</param>
    /// <returns>The normalized identifier.</returns>
    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim().Trim('"', '\'', '[', ']').TrimStart('#');

        if (text.StartsWith("id:", StringComparison.OrdinalIgnoreCase))
        {
            text = text[3..].Trim();
        }

        return text.ToUpperInvariant();
    }

    private static string Read(OpenXmlElement element)
    {
        return element switch
        {
            Paragraph paragraph => paragraph.ParagraphId?.Value,
            TableRow row => row.ParagraphId?.Value,
            _ => null,
        };
    }

    private static void Write(OpenXmlElement element, string value)
    {
        switch (element)
        {
            case Paragraph paragraph:
                paragraph.ParagraphId = value;

                break;

            case TableRow row:
                row.ParagraphId = value;

                break;
        }
    }

    private static bool IsValid(string value)
    {
        return !string.IsNullOrEmpty(value) &&
            value.Length == 8 &&
            int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number) &&
            number > 0;
    }

    private static IEnumerable<OpenXmlElement> EnumerateRoots(MainDocumentPart mainPart)
    {
        if (mainPart.Document?.Body is { } body)
        {
            yield return body;
        }

        foreach (var header in mainPart.HeaderParts)
        {
            if (header.Header is not null)
            {
                yield return header.Header;
            }
        }

        foreach (var footer in mainPart.FooterParts)
        {
            if (footer.Footer is not null)
            {
                yield return footer.Footer;
            }
        }

        if (mainPart.FootnotesPart?.Footnotes is { } footnotes)
        {
            yield return footnotes;
        }

        if (mainPart.EndnotesPart?.Endnotes is { } endnotes)
        {
            yield return endnotes;
        }

        if (mainPart.WordprocessingCommentsPart?.Comments is { } comments)
        {
            yield return comments;
        }
    }
}
