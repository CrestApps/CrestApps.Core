namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// The paper a section is printed on: its size, orientation and margins.
/// </summary>
internal sealed class PdfPageSetupDefinition
{
    /// <summary>
    /// Gets or sets a named paper size: <c>A3</c>, <c>A4</c>, <c>A5</c>, <c>B5</c>, <c>Letter</c>,
    /// <c>Legal</c>, <c>Tabloid</c> or <c>Executive</c>.
    /// </summary>
    public string Size { get; set; }

    /// <summary>
    /// Gets or sets a custom page width in millimetres, used together with <see cref="HeightMm"/> in place of
    /// <see cref="Size"/>.
    /// </summary>
    public double? WidthMm { get; set; }

    /// <summary>
    /// Gets or sets a custom page height in millimetres.
    /// </summary>
    public double? HeightMm { get; set; }

    /// <summary>
    /// Gets or sets the orientation: <c>portrait</c> or <c>landscape</c>.
    /// </summary>
    public string Orientation { get; set; }

    /// <summary>
    /// Gets or sets the top margin in millimetres.
    /// </summary>
    public double? MarginTopMm { get; set; }

    /// <summary>
    /// Gets or sets the bottom margin in millimetres.
    /// </summary>
    public double? MarginBottomMm { get; set; }

    /// <summary>
    /// Gets or sets the left margin in millimetres.
    /// </summary>
    public double? MarginLeftMm { get; set; }

    /// <summary>
    /// Gets or sets the right margin in millimetres.
    /// </summary>
    public double? MarginRightMm { get; set; }
}
