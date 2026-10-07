using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Tools;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Structure;

/// <summary>
/// Works out which top-level blocks of a document a tool call means: listed ids, a range from one id to another,
/// a heading with everything under it, or a whole section.
/// </summary>
internal static class WordBlockSelection
{
    /// <summary>
    /// The schema of the selection arguments.
    /// </summary>
    public const string Schema = """
        "ids": { "type": "array", "items": { "type": "string" }, "description": "Element ids from get_word_document." },
        "from": { "type": "string", "description": "First element id of a range (with 'to'); every element between them is included." },
        "to": { "type": "string", "description": "Last element id of a range." },
        "heading_with_content": { "type": "string", "description": "A heading id: the heading and everything under it, up to the next heading of the same or a higher level." }
        """;

    /// <summary>
    /// Reads the selection from a tool call's arguments.
    /// </summary>
    /// <remarks>
    /// An id that names a paragraph or a table inside a table cell or a content control selects that element
    /// alone when <paramref name="nested"/> is <see langword="true"/>, as removing it does; otherwise such an id
    /// is refused, so moving a paragraph out of a cell never takes the whole table with it.
    /// </remarks>
    /// <param name="package">The document.</param>
    /// <param name="arguments">The arguments.</param>
    /// <param name="nested">Whether ids may name elements inside a table cell or a content control.</param>
    /// <returns>The selected blocks, in document order, or an empty list when nothing was asked for.</returns>
    public static List<OpenXmlElement> Read(WordPackage package, WordToolArguments arguments, bool nested = false)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(arguments);

        var body = package.Body;

        if (arguments.GetString("heading_with_content") is { } heading)
        {
            return HeadingWithContent(package, heading);
        }

        if (arguments.GetString("from") is { } from)
        {
            var to = arguments.GetString("to") ?? from;

            return Range(package, from, to);
        }

        var selected = new List<OpenXmlElement>();

        foreach (var id in arguments.GetIds("ids", "id"))
        {
            var element = WordBlockLocator.Require(package, id);

            if (element.Parent is not Body && !nested)
            {
                throw new WordToolException(WordBlockLocator.IsInTable(element)
                    ? $"[{id}] is inside a table cell. Pass the table's own id to work on the whole table; the content of its cells is changed with update_word_table or update_word_content."
                    : $"[{id}] is inside a content control. Pass the id of the control itself, the first id get_word_document lists for it.");
            }

            if (!selected.Contains(element))
            {
                selected.Add(element);
            }
        }

        if (selected.All(element => element.Parent is Body))
        {
            return [.. selected.OrderBy(element => IndexOf(body, element))];
        }

        var order = body.Descendants().Select((element, index) => (element, index)).ToDictionary(pair => pair.element, pair => pair.index, ReferenceEqualityComparer.Instance);

        return [.. selected.OrderBy(element => order.GetValueOrDefault(element, -1))];
    }

    /// <summary>
    /// Returns a heading and everything under it.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <param name="id">The heading's id.</param>
    /// <returns>The blocks.</returns>
    public static List<OpenXmlElement> HeadingWithContent(WordPackage package, string id)
    {
        var element = WordBlockLocator.Require(package, id);
        var styles = new WordStyleIndex(package.MainPart);
        var level = element is Paragraph paragraph ? styles.OutlineLevelOf(paragraph) : null;

        if (level is null || element.Parent is not Body)
        {
            throw new WordToolException($"\"{id}\" is not a heading in the body of the document.");
        }

        var blocks = new List<OpenXmlElement> { element };

        for (var next = element.NextSibling(); next is not null and not SectionProperties; next = next.NextSibling())
        {
            if (next is Paragraph candidate && styles.OutlineLevelOf(candidate) is { } nextLevel && nextLevel <= level)
            {
                break;
            }

            blocks.Add(next);
        }

        return blocks;
    }

    /// <summary>
    /// Returns every top-level block from one element to another.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <param name="from">The first element's id.</param>
    /// <param name="to">The last element's id.</param>
    /// <returns>The blocks.</returns>
    public static List<OpenXmlElement> Range(WordPackage package, string from, string to)
    {
        var body = package.Body;
        var first = WordSections.TopLevel(body, WordBlockLocator.Require(package, from));
        var last = WordSections.TopLevel(body, WordBlockLocator.Require(package, to));

        if (first is null || last is null)
        {
            throw new WordToolException("A range must start and end in the body of the document.");
        }

        if (IndexOf(body, first) > IndexOf(body, last))
        {
            (first, last) = (last, first);
        }

        var blocks = new List<OpenXmlElement>();

        for (var current = first; current is not null; current = current.NextSibling())
        {
            blocks.Add(current);

            if (ReferenceEquals(current, last))
            {
                break;
            }
        }

        return blocks;
    }

    /// <summary>
    /// Returns the top-level blocks of a section, including the paragraph that ends it.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <param name="number">The one-based section number.</param>
    /// <returns>The blocks.</returns>
    public static List<OpenXmlElement> Section(WordPackage package, int number)
    {
        var sections = WordSections.All(package);

        if (number < 1 || number > sections.Count)
        {
            throw new WordToolException($"There is no section {number}; the document has {sections.Count}.");
        }

        var blocks = new List<OpenXmlElement>();
        var current = 1;

        foreach (var element in package.Body.ChildElements)
        {
            if (element is SectionProperties)
            {
                continue;
            }

            if (current == number)
            {
                blocks.Add(element);
            }

            if (element is Paragraph paragraph && paragraph.ParagraphProperties?.SectionProperties is not null)
            {
                current++;
            }
        }

        return blocks;
    }

    /// <summary>
    /// Returns a block's position among the body's children.
    /// </summary>
    /// <param name="body">The body.</param>
    /// <param name="element">The block.</param>
    /// <returns>The position, or -1.</returns>
    public static int IndexOf(Body body, OpenXmlElement element)
    {
        var index = 0;

        foreach (var child in body.ChildElements)
        {
            if (ReferenceEquals(child, element))
            {
                return index;
            }

            index++;
        }

        return -1;
    }
}
