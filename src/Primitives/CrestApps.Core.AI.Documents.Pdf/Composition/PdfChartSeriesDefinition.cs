namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// One series of a chart.
/// </summary>
internal sealed class PdfChartSeriesDefinition
{
    /// <summary>
    /// Gets or sets the series name shown in the legend.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the values, one per label. A <see langword="null"/> is a missing point.
    /// </summary>
    public List<double?> Values { get; set; } = [];

    /// <summary>
    /// Gets or sets the colour the series is drawn in, overriding the theme's chart colours.
    /// </summary>
    public string Color { get; set; }
}
