using CrestApps.Core.AI.Documents.Pdf.Graphics;
using CrestApps.Core.AI.Documents.Pdf.Objects;

namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// Lays a <see cref="PdfLayoutDocument"/> out into pages and writes them as a PDF: the body, the headers and
/// footers with their page numbers, the links, and the outline its headings make.
/// </summary>
/// <remarks>
/// A document whose fields name the page of a bookmark, as a table of contents does, is laid out twice:
/// once to find the pages, then again with them filled in.
/// </remarks>
internal sealed class PdfLayoutRenderer
{
    private readonly PdfLayoutDocument _document;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfLayoutRenderer"/> class.
    /// </summary>
    /// <param name="document">The document to lay out.</param>
    public PdfLayoutRenderer(PdfLayoutDocument document)
    {
        _document = document;
    }

    /// <summary>
    /// Lays the document out and writes it.
    /// </summary>
    /// <returns>The PDF document. The caller disposes it.</returns>
    public PdfDocument Render()
    {
        IReadOnlyDictionary<string, PdfLayoutTarget> targets = null;
        var pageCount = 0;

        if (HasPageReferences())
        {
            var measure = new PdfLayoutPass(_document, draw: false, null, 0);

            measure.Run();
            targets = measure.Targets;
            pageCount = measure.PageCount;
        }

        var pass = new PdfLayoutPass(_document, draw: true, targets, pageCount);

        pass.Run();

        return pass.Output;
    }

    private bool HasPageReferences()
    {
        return _document.Sections
            .SelectMany(section => section.Blocks)
            .OfType<PdfLayoutParagraph>()
            .SelectMany(paragraph => paragraph.Inlines)
            .Any(inline => inline is PdfLayoutField { Kind: PdfLayoutFieldKind.PageReference or PdfLayoutFieldKind.NumPages or PdfLayoutFieldKind.SectionPages });
    }
}
