namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// A number filled in once the document is laid out: the page number, the page count, or the page a
/// bookmark is on.
/// </summary>
internal sealed class PdfLayoutField : PdfLayoutInline
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfLayoutField"/> class.
    /// </summary>
    /// <param name="kind">What the field shows.</param>
    /// <param name="bookmark">The bookmark a <see cref="PdfLayoutFieldKind.PageReference"/> shows the page of.</param>
    public PdfLayoutField(PdfLayoutFieldKind kind, string bookmark = null)
    {
        Kind = kind;
        Bookmark = bookmark;
    }

    /// <summary>
    /// Gets what the field shows.
    /// </summary>
    public PdfLayoutFieldKind Kind { get; }

    /// <summary>
    /// Gets the bookmark a page reference shows the page of.
    /// </summary>
    public string Bookmark { get; }

    /// <summary>
    /// Gets or sets how the number is written: <c>roman</c>, <c>ROMAN</c>, <c>alphabetic</c>,
    /// <c>ALPHABETIC</c>, or <see langword="null"/> for digits.
    /// </summary>
    public string Format { get; set; }
}
