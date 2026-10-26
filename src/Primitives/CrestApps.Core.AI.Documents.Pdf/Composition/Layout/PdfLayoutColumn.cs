namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// A column of a <see cref="PdfLayoutTable"/>.
/// </summary>
internal sealed class PdfLayoutColumn
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfLayoutColumn"/> class.
    /// </summary>
    /// <param name="width">The width, in points.</param>
    public PdfLayoutColumn(double width)
    {
        Width = width;
    }

    /// <summary>
    /// Gets the width, in points.
    /// </summary>
    public double Width { get; }

    /// <summary>
    /// Gets the format the column's cells take after the table's.
    /// </summary>
    public PdfLayoutParagraphFormat Format { get; } = new();
}
