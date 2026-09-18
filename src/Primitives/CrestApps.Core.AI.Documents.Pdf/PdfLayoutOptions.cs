namespace CrestApps.Core.AI.Documents.Pdf;

/// <summary>
/// Controls how much structure the PDF reader recovers from a page.
/// </summary>
public sealed class PdfLayoutOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether blocks a decoration classifier identifies as running heads,
    /// footers or page furniture are left out of the document.
    /// </summary>
    public bool StripDecoration { get; set; } = true;

    /// <summary>
    /// Gets or sets the length above which a block is never treated as decoration, however often it repeats.
    /// A running head is short; a repeated legal notice is body text that happens to appear on every page.
    /// </summary>
    public int MaxDecorationCharacters { get; set; } = 200;

    /// <summary>
    /// Gets or sets a value indicating whether decoration is emitted as a header or footer element, marked
    /// with <c>ElementMetadataKeys.IsDecoration</c>, instead of being dropped.
    /// </summary>
    /// <remarks>
    /// Marked, not deleted, by default. A running head says what kind of section a page belongs to and a
    /// printed page number says what folio a citation should name, and structure analysis reads both.
    /// Everything that embeds text skips decoration, so keeping it costs nothing in the index.
    /// </remarks>
    public bool EmitDecorationAsHeaderFooter { get; set; } = true;

    /// <summary>
    /// Gets or sets the smallest image, in samples on either axis, that is worth emitting. Anything smaller
    /// is a rule, a bullet or an icon.
    /// </summary>
    public int MinImageSamples { get; set; } = 32;

    /// <summary>
    /// Gets or sets a value indicating whether images are emitted at all.
    /// </summary>
    public bool EmitImages { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether pages are segmented into blocks and put into reading order.
    /// Turning this off falls back to the raw content-stream text, one paragraph per page, which is what the
    /// reader produced before layout analysis existed.
    /// </summary>
    public bool UseLayoutAnalysis { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether tables the page rules are read as tables.
    /// </summary>
    /// <remarks>
    /// A table read as prose is a row of numbers with nothing saying which column each belongs to.
    /// </remarks>
    public bool EmitTables { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether figures the page draws, rather than places, are emitted.
    /// </summary>
    /// <remarks>
    /// A chart produced by a spreadsheet is usually not an image at all: the file contains instructions for
    /// drawing it. Reading only placed images misses every one of them.
    /// </remarks>
    public bool EmitVectorFigures { get; set; } = true;

    /// <summary>
    /// Gets or sets the most drawn line segments a page may have before drawn table and vector figure
    /// detection are skipped for it.
    /// </summary>
    /// <remarks>
    /// Grouping segments into grids and drawings compares every segment with every other. A page of dense
    /// vector art — a map, a full-page advertisement drawn as geometry — can carry tens of thousands, and
    /// reading it would take minutes for figures nobody asked about. Past this ceiling the page keeps its
    /// text, its placed images and any whitespace-aligned tables, and simply reports no drawn tables or
    /// figures.
    /// </remarks>
    public int MaxVectorSegmentsPerPage { get; set; } = 4000;
}
