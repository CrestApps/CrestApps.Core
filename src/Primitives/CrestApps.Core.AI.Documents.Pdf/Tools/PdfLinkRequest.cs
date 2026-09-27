namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// One link as <c>add_pdf_links</c> receives it.
/// </summary>
internal sealed class PdfLinkRequest
{
    /// <summary>
    /// Gets or sets the one-based page the link is placed on.
    /// </summary>
    public int? Page { get; set; }

    /// <summary>
    /// Gets or sets the left edge of the link area, in points from the left of the page.
    /// </summary>
    public double? X { get; set; }

    /// <summary>
    /// Gets or sets the top edge of the link area, in points from the top of the page.
    /// </summary>
    public double? Y { get; set; }

    /// <summary>
    /// Gets or sets the width of the link area, in points.
    /// </summary>
    public double? Width { get; set; }

    /// <summary>
    /// Gets or sets the height of the link area, in points.
    /// </summary>
    public double? Height { get; set; }

    /// <summary>
    /// Gets or sets the text the link is placed over, instead of an area.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Gets or sets the one-based match of <see cref="Text"/> to link; every match when not set.
    /// </summary>
    public int? Occurrence { get; set; }

    /// <summary>
    /// Gets or sets the web address the link opens.
    /// </summary>
    public string Url { get; set; }

    /// <summary>
    /// Gets or sets the one-based page of this document the link goes to.
    /// </summary>
    public int? TargetPage { get; set; }
}
