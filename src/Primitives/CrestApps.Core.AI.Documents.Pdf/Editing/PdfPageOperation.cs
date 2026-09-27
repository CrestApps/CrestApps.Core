using CrestApps.Core.AI.Documents.Pdf.Composition;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// One step of an <c>edit_pdf_pages</c> call. Which properties apply is decided by <see cref="Operation"/>.
/// </summary>
internal sealed class PdfPageOperation
{
    /// <summary>
    /// Gets or sets the operation: <c>merge</c>, <c>extract</c>, <c>delete</c>, <c>reorder</c>,
    /// <c>reverse</c>, <c>rotate</c>, <c>duplicate</c>, <c>insert_blank</c>, <c>crop</c>, <c>resize</c>,
    /// <c>split</c>, <c>watermark</c>, <c>stamp</c>, <c>page_numbers</c>, <c>header_footer</c> or
    /// <c>discard</c>.
    /// </summary>
    public string Operation { get; set; }

    /// <summary>
    /// Gets or sets the pages the operation applies to.
    /// </summary>
    public string Pages { get; set; }

    /// <summary>
    /// Gets or sets the PDFs a merge appends.
    /// </summary>
    public List<string> Sources { get; set; }

    /// <summary>
    /// Gets or sets where the operation places things: for a merge <c>end</c>, <c>start</c> or a page number
    /// to insert after; for a stamp or page numbers <c>top-left</c>, <c>top-center</c>, <c>top-right</c>,
    /// <c>center</c>, <c>bottom-left</c>, <c>bottom-center</c> or <c>bottom-right</c>; for a watermark
    /// <c>center</c>, <c>top</c> or <c>bottom</c>.
    /// </summary>
    public string Position { get; set; }

    /// <summary>
    /// Gets or sets the new page order of a reorder, for example <c>3,1,2,4-</c>.
    /// </summary>
    public string Order { get; set; }

    /// <summary>
    /// Gets or sets the clockwise rotation of a rotate, a multiple of 90.
    /// </summary>
    public int? Degrees { get; set; }

    /// <summary>
    /// Gets or sets how many copies a duplicate adds, or how many pages an insert adds.
    /// </summary>
    public int? Count { get; set; }

    /// <summary>
    /// Gets or sets the page a blank page is inserted after; 0 inserts it first.
    /// </summary>
    public int? After { get; set; }

    /// <summary>
    /// Gets or sets the paper size of inserted blank pages or of a resize.
    /// </summary>
    public string Size { get; set; }

    /// <summary>
    /// Gets or sets the orientation of inserted blank pages or of a resize.
    /// </summary>
    public string Orientation { get; set; }

    /// <summary>
    /// Gets or sets the margins a crop trims, in millimetres.
    /// </summary>
    public PdfPageSetupDefinition Margins { get; set; }

    /// <summary>
    /// Gets or sets the number of pages in each part of a split.
    /// </summary>
    public int? Every { get; set; }

    /// <summary>
    /// Gets or sets the page ranges of each part of a split.
    /// </summary>
    public List<string> Ranges { get; set; }

    /// <summary>
    /// Gets or sets the text of a watermark or a stamp.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Gets or sets the image of a watermark.
    /// </summary>
    public string Image { get; set; }

    /// <summary>
    /// Gets or sets the opacity of a watermark or a stamp.
    /// </summary>
    public double? Opacity { get; set; }

    /// <summary>
    /// Gets or sets the rotation of a watermark, in degrees counter-clockwise.
    /// </summary>
    public double? Rotation { get; set; }

    /// <summary>
    /// Gets or sets the font size of text drawn on the pages.
    /// </summary>
    public double? FontSize { get; set; }

    /// <summary>
    /// Gets or sets the colour of text drawn on the pages.
    /// </summary>
    public string Color { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a stamp is bold.
    /// </summary>
    public bool? Bold { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a stamp has a box around it.
    /// </summary>
    public bool? Box { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a watermark is drawn behind the content.
    /// </summary>
    public bool? Behind { get; set; }

    /// <summary>
    /// Gets or sets the text page numbers are printed in, for example <c>Page {page} of {pages}</c>.
    /// </summary>
    public string Template { get; set; }

    /// <summary>
    /// Gets or sets the number the first numbered page carries.
    /// </summary>
    public int? StartAt { get; set; }

    /// <summary>
    /// Gets or sets the numbering style: <c>1</c>, <c>i</c>, <c>I</c>, <c>a</c> or <c>A</c>.
    /// </summary>
    public string Format { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the first page is left unnumbered.
    /// </summary>
    public bool? SkipFirst { get; set; }

    /// <summary>
    /// Gets or sets the running head text of a header_footer.
    /// </summary>
    public PdfHeaderFooterDefinition Header { get; set; }

    /// <summary>
    /// Gets or sets the running foot text of a header_footer.
    /// </summary>
    public PdfHeaderFooterDefinition Footer { get; set; }

    /// <summary>
    /// Gets or sets the distance from the page edge of text drawn on the pages, in points.
    /// </summary>
    public double? Margin { get; set; }
}
