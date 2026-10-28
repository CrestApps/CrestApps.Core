namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// What a <see cref="PdfLayoutField"/> shows.
/// </summary>
internal enum PdfLayoutFieldKind
{
    /// <summary>
    /// The number of the page it is on.
    /// </summary>
    Page,

    /// <summary>
    /// The number of pages in the document.
    /// </summary>
    NumPages,

    /// <summary>
    /// The number of pages in the section it is in.
    /// </summary>
    SectionPages,

    /// <summary>
    /// The number of the page a bookmark is on.
    /// </summary>
    PageReference,
}
