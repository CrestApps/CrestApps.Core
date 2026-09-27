namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// The table style a theme applies to every table that does not style itself.
/// </summary>
internal sealed class PdfTableStyleDefinition
{
    /// <summary>
    /// Gets or sets the header row fill colour.
    /// </summary>
    public string HeaderBackground { get; set; }

    /// <summary>
    /// Gets or sets the header row text colour.
    /// </summary>
    public string HeaderTextColor { get; set; }

    /// <summary>
    /// Gets or sets the colour of the cell borders.
    /// </summary>
    public string BorderColor { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether alternating rows are shaded.
    /// </summary>
    public bool? Banded { get; set; }

    /// <summary>
    /// Gets or sets the fill colour of the shaded rows.
    /// </summary>
    public string BandColor { get; set; }

    /// <summary>
    /// Gets or sets the table font size in points.
    /// </summary>
    public double? FontSize { get; set; }

    /// <summary>
    /// Gets or sets which borders are drawn: <c>all</c>, <c>horizontal</c> or <c>none</c>.
    /// </summary>
    public string Borders { get; set; }
}
