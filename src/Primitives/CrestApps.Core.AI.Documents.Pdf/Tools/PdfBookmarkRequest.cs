namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// One bookmark as <c>add_pdf_bookmarks</c> receives it.
/// </summary>
internal sealed class PdfBookmarkRequest
{
    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the one-based page the bookmark opens.
    /// </summary>
    public int? Page { get; set; }

    /// <summary>
    /// Gets or sets the one-based nesting level; a bookmark nests under the closest earlier one with a
    /// lower level.
    /// </summary>
    public int? Level { get; set; }

    /// <summary>
    /// Gets or sets where on the page the view starts, in points from the top of the page.
    /// </summary>
    public double? Y { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the title is bold.
    /// </summary>
    public bool? Bold { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the title is italic.
    /// </summary>
    public bool? Italic { get; set; }

    /// <summary>
    /// Gets or sets the title's colour.
    /// </summary>
    public string Color { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the bookmark's children are shown expanded.
    /// </summary>
    public bool? Open { get; set; }

    /// <summary>
    /// Gets or sets the bookmarks nested under this one.
    /// </summary>
    public List<PdfBookmarkRequest> Children { get; set; }
}
