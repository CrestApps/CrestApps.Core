namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// A table: columns of set widths and rows of cells holding paragraphs. Heading rows repeat at the top of
/// every page the table runs onto; a row is never split across pages.
/// </summary>
internal sealed class PdfLayoutTable : PdfLayoutBlock
{
    /// <summary>
    /// Gets the columns.
    /// </summary>
    public List<PdfLayoutColumn> Columns { get; } = [];

    /// <summary>
    /// Gets the rows.
    /// </summary>
    public List<PdfLayoutRow> Rows { get; } = [];

    /// <summary>
    /// Gets the format every cell's paragraphs take before their column's, row's and own.
    /// </summary>
    public PdfLayoutParagraphFormat Format { get; } = new();

    /// <summary>
    /// Gets the borders every cell takes before its row's and own.
    /// </summary>
    public PdfLayoutBorders Borders { get; } = new();

    /// <summary>
    /// Gets or sets the space between a cell's left edge and its content, in points.
    /// </summary>
    public double LeftPadding { get; set; } = 3.4;

    /// <summary>
    /// Gets or sets the space between a cell's right edge and its content, in points.
    /// </summary>
    public double RightPadding { get; set; } = 3.4;

    /// <summary>
    /// Gets or sets the space between a cell's top edge and its content, in points.
    /// </summary>
    public double TopPadding { get; set; }

    /// <summary>
    /// Gets or sets the space between a cell's bottom edge and its content, in points.
    /// </summary>
    public double BottomPadding { get; set; }

    /// <summary>
    /// Gets or sets whether the table is moved to the next page rather than split, when it fits on one page.
    /// </summary>
    public bool KeepTogether { get; set; }

    /// <summary>
    /// Gets or sets how far the table's left edge is from the left margin, in points; without one it is moved
    /// left by its padding, so the text in its first column lines up with the paragraphs around it.
    /// </summary>
    public double? LeftIndent { get; set; }

    /// <summary>
    /// Adds a column.
    /// </summary>
    /// <param name="width">The width, in points.</param>
    /// <returns>The column.</returns>
    public PdfLayoutColumn AddColumn(double width)
    {
        var column = new PdfLayoutColumn(width);

        Columns.Add(column);

        return column;
    }

    /// <summary>
    /// Adds a row with a cell for every column.
    /// </summary>
    /// <returns>The row.</returns>
    public PdfLayoutRow AddRow()
    {
        var row = new PdfLayoutRow(Columns.Count);

        Rows.Add(row);

        return row;
    }
}
