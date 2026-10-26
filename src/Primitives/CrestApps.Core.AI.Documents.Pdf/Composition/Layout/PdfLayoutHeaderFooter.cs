namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// A header or a footer: paragraphs set on every page it applies to, with their fields filled in page by page.
/// </summary>
internal sealed class PdfLayoutHeaderFooter
{
    /// <summary>
    /// Gets the paragraphs.
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
