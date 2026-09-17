namespace CrestApps.Core.AI.Documents.Generation.RichText;

/// <summary>
/// The kind of block in a parsed document.
/// </summary>
public enum RichTextBlockKind
{
    /// <summary>
    /// A run of body text.
    /// </summary>
    Paragraph,

    /// <summary>
    /// A section heading. <see cref="RichTextBlock.Level"/> carries its depth, 1 through 6.
    /// </summary>
    Heading,

    /// <summary>
    /// An item in a bulleted list.
    /// </summary>
    BulletItem,

    /// <summary>
    /// An item in a numbered list. <see cref="RichTextBlock.Number"/> carries its position.
    /// </summary>
    NumberedItem,

    /// <summary>
    /// A quoted passage.
    /// </summary>
    Quote,

    /// <summary>
    /// A preformatted code block. The content is in <see cref="RichTextBlock.Text"/> rather than in
    /// spans, because code is never re-formatted.
    /// </summary>
    Code,

    /// <summary>
    /// A horizontal rule separating sections.
    /// </summary>
    HorizontalRule,

    /// <summary>
    /// A table. The content is in <see cref="RichTextBlock.Table"/>.
    /// </summary>
    Table,
}

/// <summary>
/// A run of text within a block that shares one set of inline formatting.
/// </summary>
public sealed class RichTextSpan
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RichTextSpan"/> class.
    /// </summary>
    /// <param name="text">The literal text.</param>
    public RichTextSpan(string text)
    {
        Text = text ?? string.Empty;
    }

    /// <summary>
    /// Gets the literal text, with any markup that produced the formatting already removed.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the run is bold.
    /// </summary>
    public bool Bold { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the run is italic.
    /// </summary>
    public bool Italic { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the run is struck through.
    /// </summary>
    public bool Strikethrough { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the run is inline code.
    /// </summary>
    public bool Code { get; set; }

    /// <summary>
    /// Gets or sets the target of a link, when the run is a link.
    /// </summary>
    public string Link { get; set; }
}

/// <summary>
/// One cell of a parsed table.
/// </summary>
public sealed class RichTextCell
{
    /// <summary>
    /// Gets the formatted runs that make up the cell.
    /// </summary>
    public IList<RichTextSpan> Spans { get; init; } = [];
}

/// <summary>
/// One row of a parsed table.
/// </summary>
public sealed class RichTextRow
{
    /// <summary>
    /// Gets the cells in the row.
    /// </summary>
    public IList<RichTextCell> Cells { get; init; } = [];
}

/// <summary>
/// A parsed table.
/// </summary>
public sealed class RichTextTable
{
    /// <summary>
    /// Gets the header row.
    /// </summary>
    public RichTextRow Header { get; init; } = new();

    /// <summary>
    /// Gets the data rows.
    /// </summary>
    public IList<RichTextRow> Rows { get; init; } = [];

    /// <summary>
    /// Gets the widest row in the table, which is how many columns a renderer must lay out.
    /// </summary>
    public int ColumnCount
    {
        get
        {
            var columns = Header.Cells.Count;

            foreach (var row in Rows)
            {
                columns = Math.Max(columns, row.Cells.Count);
            }

            return columns;
        }
    }
}

/// <summary>
/// One block of a parsed document.
/// </summary>
public sealed class RichTextBlock
{
    /// <summary>
    /// Gets the block kind.
    /// </summary>
    public RichTextBlockKind Kind { get; init; }

    /// <summary>
    /// Gets the heading depth, 1 through 6, for a heading block.
    /// </summary>
    public int Level { get; init; }

    /// <summary>
    /// Gets the position of a numbered list item.
    /// </summary>
    public int Number { get; init; }

    /// <summary>
    /// Gets the formatted runs that make up the block.
    /// </summary>
    public IList<RichTextSpan> Spans { get; init; } = [];

    /// <summary>
    /// Gets the verbatim content of a code block.
    /// </summary>
    public string Text { get; init; }

    /// <summary>
    /// Gets the table content of a table block.
    /// </summary>
    public RichTextTable Table { get; init; }
}
