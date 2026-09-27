namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// One series of a chart to write.
/// </summary>
internal sealed class OpenXmlChartSeriesDefinition
{
    /// <summary>
    /// Gets or sets the name shown in the legend.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the values, one per category.
    /// </summary>
    public List<double?> Values { get; set; } = [];

    /// <summary>
    /// Gets or sets the horizontal values of a scatter series.
    /// </summary>
    public List<double?> XValues { get; set; } = [];

    /// <summary>
    /// Gets or sets the colour, or <see langword="null"/> to take it from the palette.
    /// </summary>
    public string Color { get; set; }
}
