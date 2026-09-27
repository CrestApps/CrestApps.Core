namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// A running head or foot, in three slots across the page.
/// </summary>
/// <remarks>
/// Each slot is text that may carry the tokens <c>{page}</c>, <c>{pages}</c>, <c>{title}</c>,
/// <c>{author}</c> and <c>{date}</c>. <c>{page}</c> and <c>{pages}</c> are written as fields the renderer
/// fills in page by page, so the numbers are right however the content reflows.
/// </remarks>
internal sealed class PdfHeaderFooterDefinition
{
    /// <summary>
    /// Gets or sets the text printed at the left edge.
    /// </summary>
    public string Left { get; set; }

    /// <summary>
    /// Gets or sets the text printed in the centre.
    /// </summary>
    public string Center { get; set; }

    /// <summary>
    /// Gets or sets the text printed at the right edge.
    /// </summary>
    public string Right { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a thin rule separates the head or foot from the body.
    /// </summary>
    public bool? Separator { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the head or foot is also printed on the first body page.
    /// </summary>
    public bool? ShowOnFirstPage { get; set; }

    /// <summary>
    /// Gets or sets the font size in points.
    /// </summary>
    public double? FontSize { get; set; }

    /// <summary>
    /// Gets or sets the text colour.
    /// </summary>
    public string Color { get; set; }
}
