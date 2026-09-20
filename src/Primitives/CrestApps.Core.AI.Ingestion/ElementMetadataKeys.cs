namespace CrestApps.Core.AI.Ingestion;

/// <summary>
/// The metadata keys readers and processors use on any <see cref="Microsoft.Extensions.DataIngestion.IngestionDocumentElement"/>.
/// Every key is written and read through these constants so no string literal can drift.
/// </summary>
public static class ElementMetadataKeys
{
    /// <summary>
    /// The element bounds in PDF user space, as a <see cref="double"/> array of left, bottom, right and top.
    /// </summary>
    public const string BoundingBox = "crestapps.boundingBox";

    /// <summary>
    /// The most common font size, in points, among the letters that make up the element.
    /// </summary>
    public const string ModalPointSize = "crestapps.modalPointSize";

    /// <summary>
    /// The most common font name among the letters that make up the element.
    /// </summary>
    public const string ModalFontName = "crestapps.modalFontName";

    /// <summary>
    /// Marks an element a decoration classifier identified as a running head, footer or page furniture.
    /// </summary>
    public const string IsDecoration = "crestapps.isDecoration";

    /// <summary>
    /// The identifier of the figure an element captions, so flattening can emit the caption with its figure
    /// rather than twice.
    /// </summary>
    public const string IsCaptionFor = "crestapps.isCaptionFor";

    /// <summary>
    /// The page number printed on the page, which is not the same as the page index within the file.
    /// </summary>
    public const string Folio = "crestapps.folio";

    /// <summary>
    /// The running-head label that identifies the kind of section a page belongs to.
    /// </summary>
    public const string SectionLabel = "crestapps.sectionLabel";

    /// <summary>
    /// The one-based position of the article an element belongs to within its document.
    /// </summary>
    public const string ArticleOrdinal = "crestapps.articleOrdinal";

    /// <summary>
    /// How many images on a page were left out because nothing here can decode them. Recorded on the
    /// section so a corpus that needs another codec is visible rather than silently thinner.
    /// </summary>
    public const string UnsupportedImageCount = "crestapps.unsupportedImageCount";

    /// <summary>
    /// The page width in user-space units, recorded on the section so a figure's share of the page can be
    /// measured without re-reading the file.
    /// </summary>
    public const string PageWidth = "crestapps.pageWidth";

    /// <summary>
    /// The page height in user-space units, recorded on the section alongside the width.
    /// </summary>
    public const string PageHeight = "crestapps.pageHeight";

    /// <summary>
    /// The document's own outline, as an
    /// <see cref="IReadOnlyList{T}"/> of <see cref="Knowledge.Structure.DocumentOutlineEntry"/> in document
    /// order.
    /// </summary>
    /// <remarks>
    /// Recorded on the <em>first</em> section rather than per page, because it describes the whole file and
    /// splitting it across the pages it points at would mean reassembling an order the document already
    /// stated. An <c>IngestionDocument</c> carries no metadata of its own — only its sections and elements
    /// do — so the first section is where a document-wide fact has to live.
    /// </remarks>
    public const string Outline = "crestapps.outline";

    /// <summary>
    /// The heading level an element states it is, as a one-based <see cref="int"/> where one is the
    /// outermost heading.
    /// </summary>
    /// <remarks>
    /// This is for formats that name their own headings rather than implying them with type size: a Word
    /// paragraph styled <c>Heading 2</c>, an <c>h2</c> element, a tagged PDF's <c>H2</c>, a layout service
    /// reporting a section heading. One key for all of them is what lets a single strategy divide a
    /// document without knowing which reader produced it.
    /// <para>
    /// A stated level is never guessed at. A reader that cannot tell a heading from body text writes
    /// nothing here, and the analyzer falls through to the signals it can read.
    /// </para>
    /// </remarks>
    public const string HeadingLevel = "crestapps.headingLevel";
}
