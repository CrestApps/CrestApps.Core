using CrestApps.Core.AI.Documents.Pdf.Composition;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// A bookmark to write into a PDF's outline, with the bookmarks nested under it.
/// </summary>
internal sealed class PdfBookmarkNode
{
    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the one-based page the bookmark opens.
    /// </summary>
    public int Page { get; set; }

    /// <summary>
    /// Gets or sets where on the page the view starts, in user space (points from the bottom), or
    /// <see langword="null"/> for the top of the page.
    /// </summary>
    public double? Top { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the title is shown in bold.
    /// </summary>
    public bool Bold { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the title is shown in italics.
    /// </summary>
    public bool Italic { get; set; }

    /// <summary>
    /// Gets or sets the colour of the title, or <see langword="null"/> for the viewer's default.
    /// </summary>
    public PdfColor? Color { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the bookmark's children are shown expanded.
    /// </summary>
    public bool Open { get; set; }

    /// <summary>
    /// Gets the bookmarks nested under this one.
    /// </summary>
    public List<PdfBookmarkNode> Children { get; } = [];

    /// <summary>
    /// Counts this bookmark and every bookmark nested under it.
    /// </summary>
    /// <returns>The count.</returns>
    public int Count()
    {
        return 1 + Children.Sum(child => child.Count());
    }
}
