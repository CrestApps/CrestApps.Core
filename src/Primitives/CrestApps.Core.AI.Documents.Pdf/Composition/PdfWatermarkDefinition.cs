namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// A text or image watermark drawn across pages.
/// </summary>
internal sealed class PdfWatermarkDefinition
{
    /// <summary>
    /// Gets or sets the watermark text, for example <c>DRAFT</c> or <c>CONFIDENTIAL</c>.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Gets or sets the image drawn as the watermark instead of text.
    /// </summary>
    public string Image { get; set; }

    /// <summary>
    /// Gets or sets the opacity, from 0 (invisible) to 1 (solid).
    /// </summary>
    public double? Opacity { get; set; }

    /// <summary>
    /// Gets or sets the rotation in degrees, counter-clockwise. Defaults to the page diagonal.
    /// </summary>
    public double? Rotation { get; set; }

    /// <summary>
    /// Gets or sets the font size in points. Defaults to a size that spans most of the page.
    /// </summary>
    public double? FontSize { get; set; }

    /// <summary>
    /// Gets or sets the text colour.
    /// </summary>
    public string Color { get; set; }

    /// <summary>
    /// Gets or sets where the watermark sits: <c>center</c>, <c>top</c> or <c>bottom</c>.
    /// </summary>
    public string Position { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the watermark is drawn behind the content rather than over it.
    /// </summary>
    public bool? Behind { get; set; }
}
