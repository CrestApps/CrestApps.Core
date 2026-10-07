namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// The look a document is drawn with: its colours, its fonts, its logo and how its tables are styled.
/// </summary>
/// <remarks>
/// Kept separate from the content so "use our colours" is one change that reaches every heading, table and
/// chart already in the document, instead of an instruction the model has to reapply block by block.
/// </remarks>
internal sealed class PdfThemeDefinition
{
    /// <summary>
    /// Gets or sets the brand colour used for headings, table headers, rules and the first chart series.
    /// </summary>
    public string PrimaryColor { get; set; }

    /// <summary>
    /// Gets or sets the second colour, used for accents and the second chart series.
    /// </summary>
    public string AccentColor { get; set; }

    /// <summary>
    /// Gets or sets the body text colour.
    /// </summary>
    public string TextColor { get; set; }

    /// <summary>
    /// Gets or sets the heading colour. Defaults to <see cref="PrimaryColor"/>.
    /// </summary>
    public string HeadingColor { get; set; }

    /// <summary>
    /// Gets or sets the colour of secondary text such as captions, headers and footers.
    /// </summary>
    public string MutedColor { get; set; }

    /// <summary>
    /// Gets or sets the body font family.
    /// </summary>
    public string FontFamily { get; set; }

    /// <summary>
    /// Gets or sets the heading font family. Defaults to <see cref="FontFamily"/>.
    /// </summary>
    public string HeadingFontFamily { get; set; }

    /// <summary>
    /// Gets or sets the body font size in points.
    /// </summary>
    public double? BaseFontSize { get; set; }

    /// <summary>
    /// Gets or sets the line spacing as a multiple of the font size, for example <c>1.15</c>.
    /// </summary>
    public double? LineSpacing { get; set; }

    /// <summary>
    /// Gets or sets the image printed as the logo: an uploaded image, a workspace asset, or a figure.
    /// </summary>
    public string Logo { get; set; }

    /// <summary>
    /// Gets or sets where the logo is printed on body pages: <c>header-left</c>, <c>header-right</c>,
    /// <c>footer-left</c>, <c>footer-right</c> or <c>none</c> (cover page only).
    /// </summary>
    public string LogoPosition { get; set; }

    /// <summary>
    /// Gets or sets the logo height in points.
    /// </summary>
    public double? LogoHeight { get; set; }

    /// <summary>
    /// Gets or sets the colours chart series are drawn with, in order.
    /// </summary>
    public List<string> ChartColors { get; set; }

    /// <summary>
    /// Gets or sets how tables are styled when a table does not say otherwise.
    /// </summary>
    public PdfTableStyleDefinition Tables { get; set; }
}
