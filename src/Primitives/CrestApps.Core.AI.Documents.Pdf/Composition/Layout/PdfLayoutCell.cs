namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// A cell of a <see cref="PdfLayoutRow"/>.
/// </summary>
internal sealed class PdfLayoutCell
{
    /// <summary>
    /// Gets the paragraphs.
    /// </summary>
    public List<PdfLayoutParagraph> Paragraphs { get; } = [];

    /// <summary>
    /// Gets or sets the background colour, over the row's.
    /// </summary>
    public PdfColor? Shading { get; set; }

    /// <summary>
    /// Gets the borders the cell sets over its row's.
    /// </summary>
    public PdfLayoutBorders Borders { get; } = new();

    /// <summary>
    /// Adds a paragraph.
    /// </summary>
    /// <param name="text">Text the paragraph starts with, or <see langword="null"/>.</param>
    /// <returns>The paragraph.</returns>
    public PdfLayoutParagraph AddParagraph(string text = null)
    {
        var paragraph = new PdfLayoutParagraph();

        if (text is not null)
        {
            paragraph.AddText(text);
        }

        Paragraphs.Add(paragraph);

        return paragraph;
    }
}
