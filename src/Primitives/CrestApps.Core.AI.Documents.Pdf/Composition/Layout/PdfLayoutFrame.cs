namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// A box placed at a fixed position on the page where it is reached, behind the text around it and taking
/// no room from it: a coloured background, a band, a note at the foot of the page.
/// </summary>
internal sealed class PdfLayoutFrame : PdfLayoutBlock
{
    /// <summary>
    /// Gets or sets whether <see cref="Left"/> is measured from the page's edge rather than its left margin.
    /// </summary>
    public bool LeftFromPage { get; set; }

    /// <summary>
    /// Gets or sets whether <see cref="Top"/> is measured from the page's edge rather than its top margin.
    /// </summary>
    public bool TopFromPage { get; set; }

    /// <summary>
    /// Gets or sets the left edge, in points.
    /// </summary>
    public double Left { get; set; }

    /// <summary>
    /// Gets or sets the top edge, in points.
    /// </summary>
    public double Top { get; set; }

    /// <summary>
    /// Gets or sets the width, in points.
    /// </summary>
    public double Width { get; set; }

    /// <summary>
    /// Gets or sets the height, in points.
    /// </summary>
    public double Height { get; set; }

    /// <summary>
    /// Gets or sets the colour the box is filled with, or <see langword="null"/> to leave it clear.
    /// </summary>
    public PdfColor? Fill { get; set; }

    /// <summary>
    /// Gets the paragraphs set in the box.
    /// </summary>
    public List<PdfLayoutParagraph> Paragraphs { get; } = [];

    /// <summary>
    /// Adds a paragraph.
    /// </summary>
    /// <returns>The paragraph.</returns>
    public PdfLayoutParagraph AddParagraph()
    {
        var paragraph = new PdfLayoutParagraph();

        Paragraphs.Add(paragraph);

        return paragraph;
    }
}
