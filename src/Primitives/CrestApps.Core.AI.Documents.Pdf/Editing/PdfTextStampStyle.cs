using CrestApps.Core.AI.Documents.Pdf.Composition;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// How a line of text stamped onto a page is drawn.
/// </summary>
internal sealed class PdfTextStampStyle
{
    /// <summary>
    /// Gets or sets the font family.
    /// </summary>
    public string FontFamily { get; set; } = PdfFontFamilies.Default;

    /// <summary>
    /// Gets or sets the font size in points.
    /// </summary>
    public double FontSize { get; set; } = 9;

    /// <summary>
    /// Gets or sets a value indicating whether the text is bold.
    /// </summary>
    public bool Bold { get; set; }

    /// <summary>
    /// Gets or sets the colour.
    /// </summary>
    public PdfColor Color { get; set; } = new(0x40, 0x40, 0x40);

    /// <summary>
    /// Gets or sets the opacity, from 0 to 1.
    /// </summary>
    public double Opacity { get; set; } = 1;

    /// <summary>
    /// Gets or sets the distance from the page edge, in points.
    /// </summary>
    public double Margin { get; set; } = 28;

    /// <summary>
    /// Gets or sets a value indicating whether a box is drawn around the text, the way a rubber stamp looks.
    /// </summary>
    public bool Box { get; set; }
}
