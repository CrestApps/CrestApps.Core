using System.Globalization;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Editing;

/// <summary>
/// Finds the elements a tool call names by id, and places new content relative to them.
/// </summary>
internal static class WordBlockLocator
{
    /// <summary>
    /// Finds the block an id names: a paragraph, a table (named by its first row), or a content control such as
    /// a table of contents (named by its first paragraph). A paragraph inside a table cell is found too.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <param name="id">The id.</param>
    /// <returns>The block, or <see langword="null"/>.</returns>
    public static OpenXmlElement Find(WordPackage package, string id)
    {
        ArgumentNullException.ThrowIfNull(package);

        var wanted = WordParagraphIds.Normalize(id);

        if (string.IsNullOrEmpty(wanted))
        {
            return null;
        }

        _ = package.Ids;

        // Descendants are visited parent first, so a table is found before its first row and a content
        // control before its first paragraph.
        foreach (var element in package.Body.Descendants())
        {
            if (element is not (Paragraph or Table or SdtBlock))
            {
                continue;
            }

            if (string.Equals(WordParagraphIds.Of(element), wanted, StringComparison.OrdinalIgnoreCase))
            {
                return element;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the block an id names, or explains that there is none.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <param name="id">The id.</param>
    /// <returns>The block.</returns>
    public static OpenXmlElement Require(WordPackage package, string id)
    {
        return Find(package, id)
            ?? throw new WordToolException($"There is no element with id \"{id}\". Call get_word_document to see the current ids.");
    }

    /// <summary>
    /// Finds a table by its id, by the id of any paragraph in it, or by its one-based position in the document.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <param name="reference">The id, or a number such as <c>2</c>.</param>
    /// <returns>The table.</returns>
    public static Table RequireTable(WordPackage package, string reference)
    {
        ArgumentNullException.ThrowIfNull(package);

        var tables = package.Body.Descendants<Table>().ToList();

        if (tables.Count == 0)
        {
            throw new WordToolException("The document has no tables.");
        }

        if (string.IsNullOrWhiteSpace(reference))
        {
            if (tables.Count == 1)
            {
                return tables[0];
            }

            throw new WordToolException($"The document has {tables.Count} tables; pass 'table' with a table's id or its number (1 to {tables.Count}).");
        }

        if (int.TryParse(reference.Trim().TrimStart('#'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && reference.Trim().Length < 6)
        {
            return number >= 1 && number <= tables.Count
                ? tables[number - 1]
                : throw new WordToolException($"There is no table {number}; the document has {tables.Count}.");
        }

        var element = Find(package, reference);

        return element as Table
            ?? element?.Ancestors<Table>().FirstOrDefault()
            ?? throw new WordToolException($"\"{reference}\" is not a table or a paragraph in one. Call get_word_document to see the table ids.");
    }

    /// <summary>
    /// Inserts elements where a tool call asked for them.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <param name="elements">The elements, in order.</param>
    /// <param name="after">The id of the element they follow, or <see langword="null"/>.</param>
    /// <param name="before">The id of the element they precede, or <see langword="null"/>.</param>
    /// <param name="at"><c>start</c> or <c>end</c> of the document, used when neither id is given. Defaults to the end.</param>
    public static void Insert(WordPackage package, IReadOnlyList<OpenXmlElement> elements, string after, string before, string at)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(elements);

        if (elements.Count == 0)
        {
            return;
        }

        foreach (var element in elements)
        {
            package.Ids.Assign(element);
        }

        if (!string.IsNullOrWhiteSpace(after))
        {
            var anchor = Require(package, after);
            var reference = InsertionPoint(anchor, elements);

            for (var index = elements.Count - 1; index >= 0; index--)
            {
                reference.InsertAfterSelf(elements[index]);
            }

            RepairCell(package, reference.Ancestors<TableCell>().FirstOrDefault());

            return;
        }

        if (!string.IsNullOrWhiteSpace(before))
        {
            var anchor = InsertionPoint(Require(package, before), elements);

            foreach (var element in elements)
            {
                anchor.InsertBeforeSelf(element);
            }

            RepairCell(package, anchor.Ancestors<TableCell>().FirstOrDefault());

            return;
        }

        var body = package.Body;

        if (string.Equals(at?.Trim(), "start", StringComparison.OrdinalIgnoreCase) || string.Equals(at?.Trim(), "beginning", StringComparison.OrdinalIgnoreCase))
        {
            var first = body.FirstChild;

            foreach (var element in elements)
            {
                if (first is null)
                {
                    body.Append(element);
                }
                else
                {
                    first.InsertBeforeSelf(element);
                }
            }

            return;
        }

        var section = WordSections.EnsureBodySection(body);

        foreach (var element in elements)
        {
            section.InsertBeforeSelf(element);
        }
    }

    /// <summary>
    /// Returns whether an element sits inside a table cell.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns><see langword="true"/> when the element is in a table.</returns>
    public static bool IsInTable(OpenXmlElement element)
    {
        return element?.Ancestors<TableCell>().Any() == true;
    }

    /// <summary>
    /// Makes a table cell end with a paragraph, as Word requires of every cell: one whose last block is a table,
    /// or that has no block left, gets an empty paragraph at its end. Call it after content in a cell was
    /// inserted, removed or moved.
    /// </summary>
    /// <param name="package">The document, which gives the new paragraph its id.</param>
    /// <param name="cell">The cell, or <see langword="null"/> when the content was not in a cell.</param>
    public static void RepairCell(WordPackage package, TableCell cell)
    {
        ArgumentNullException.ThrowIfNull(package);

        if (cell is null || EndsWithParagraph(cell))
        {
            return;
        }

        var paragraph = new Paragraph();

        package.Ids.Assign(paragraph);
        cell.Append(paragraph);
    }

    private static bool EndsWithParagraph(OpenXmlElement container)
    {
        // A content control or custom XML block that ends with a paragraph ends the cell with one too.
        var last = container.ChildElements.LastOrDefault(child => child is Paragraph or Table or SdtBlock or CustomXmlBlock or AltChunk);

        return last switch
        {
            Paragraph => true,
            SdtBlock control => control.SdtContentBlock is { } content && EndsWithParagraph(content),
            CustomXmlBlock custom => EndsWithParagraph(custom),
            _ => false,
        };
    }

    private static OpenXmlElement InsertionPoint(OpenXmlElement anchor, IReadOnlyList<OpenXmlElement> elements)
    {
        // A paragraph inside a cell takes paragraphs and tables next to it in the cell. Anything else lands
        // next to the table the cell belongs to, and content placed relative to a list item inside a content
        // control lands next to the control.
        if (anchor is Paragraph && IsInTable(anchor) && elements.All(element => element is Paragraph or Table))
        {
            return anchor;
        }

        var current = anchor;

        while (current.Parent is not null and not Body && current.Parent is not TableCell)
        {
            current = current.Parent;
        }

        if (current.Parent is TableCell)
        {
            current = current.Ancestors<Table>().Last();

            while (current.Parent is not null and not Body)
            {
                current = current.Parent;
            }
        }

        return current;
    }
}
