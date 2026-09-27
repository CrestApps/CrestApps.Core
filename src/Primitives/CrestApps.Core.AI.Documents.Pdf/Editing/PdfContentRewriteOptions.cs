using CrestApps.Core.AI.Documents.Pdf.Analysis;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// What <see cref="PdfContentRewriter"/> removes from a page's content.
/// </summary>
internal sealed class PdfContentRewriteOptions
{
    /// <summary>
    /// Gets or sets the areas, in user space, whose glyphs and images are removed.
    /// </summary>
    public IReadOnlyList<PdfBox> Areas { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether text drawn invisibly (rendering mode 3) is removed.
    /// </summary>
    public bool RemoveInvisibleText { get; set; }

    /// <summary>
    /// Gets or sets the optional-content group references whose marked content is removed.
    /// </summary>
    public IReadOnlySet<PdfSharp.Pdf.PdfObject> RemoveOptionalContent { get; set; }
}
