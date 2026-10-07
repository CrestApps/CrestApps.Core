using DocumentFormat.OpenXml;

namespace CrestApps.Core.AI.Documents.Word.Reading;

/// <summary>
/// One block of a document as the tools describe it: what it is, its id, its text and where it sits.
/// </summary>
internal sealed class WordBlock
{
    /// <summary>
    /// Gets or sets the id tools name the block by.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Gets or sets what the block is.
    /// </summary>
    public WordBlockKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the element: a paragraph, a table or a content control.
    /// </summary>
    public OpenXmlElement Element { get; set; }

    /// <summary>
    /// Gets or sets the visible text.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Gets or sets the paragraph style id.
    /// </summary>
    public string StyleId { get; set; }

    /// <summary>
    /// Gets or sets the paragraph style name.
    /// </summary>
    public string StyleName { get; set; }

    /// <summary>
    /// Gets or sets the heading level from 1, or the list level from 0.
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Gets or sets the one-based section the block is in.
    /// </summary>
    public int Section { get; set; }

    /// <summary>
    /// Gets or sets the block's position among the body's blocks, from 0.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Gets or sets the number of rows of a table.
    /// </summary>
    public int Rows { get; set; }

    /// <summary>
    /// Gets or sets the number of columns of a table.
    /// </summary>
    public int Columns { get; set; }

    /// <summary>
    /// Gets or sets the drawings the block holds: pictures, charts, shapes.
    /// </summary>
    public List<WordDrawingInfo> Drawings { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the block ends a section.
    /// </summary>
    public bool EndsSection { get; set; }

    /// <summary>
    /// Gets or sets the name of the content control the block sits in — its title or tag, or an empty string when
    /// it has neither — or <see langword="null"/> for a block that is not in one.
    /// </summary>
    public string ContentControl { get; set; }

    /// <summary>
    /// Gets a value indicating whether the block is a heading or the title.
    /// </summary>
    public bool IsHeading => Kind is WordBlockKind.Heading or WordBlockKind.Title;
}
