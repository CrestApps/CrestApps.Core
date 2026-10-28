namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// A row of a <see cref="PdfLayoutTable"/>.
/// </summary>
internal sealed class PdfLayoutRow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfLayoutRow"/> class.
    /// </summary>
    /// <param name="columns">The number of cells.</param>
    public PdfLayoutRow(int columns)
    {
        Cells = [.. Enumerable.Range(0, columns).Select(_ => new PdfLayoutCell())];
    }

    /// <summary>
    /// Gets the cells, one per column.
    /// </summary>
    public IReadOnlyList<PdfLayoutCell> Cells { get; }

    /// <summary>
    /// Gets or sets whether the row is a heading row, repeated at the top of every page the table runs onto.
    /// </summary>
    public bool HeadingFormat { get; set; }

    /// <summary>
    /// Gets or sets the background colour of the row's cells.
    /// </summary>
    public PdfColor? Shading { get; set; }

    /// <summary>
    /// Gets the borders the row's cells take after the table's.
    /// </summary>
    public PdfLayoutBorders Borders { get; } = new();

    /// <summary>
    /// Gets the format the row's cells take after the table's and the column's.
    /// </summary>
    public PdfLayoutParagraphFormat Format { get; } = new();

    /// <summary>
    /// Gets or sets where the content of the row's cells sits.
    /// </summary>
    public PdfLayoutVerticalAlignment VerticalAlignment { get; set; }
}
