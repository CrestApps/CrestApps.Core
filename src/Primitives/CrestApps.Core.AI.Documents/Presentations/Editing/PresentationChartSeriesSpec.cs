namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// One data series of a chart to write.
/// </summary>
public sealed class PresentationChartSeriesSpec
{
    /// <summary>
    /// Gets or sets the series name shown in the legend.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the values, one per category. A missing point is <see langword="null"/>.
    /// </summary>
    public IList<double?> Values { get; set; } = [];

    /// <summary>
    /// Gets or sets the horizontal values of a scatter series.
    /// </summary>
    public IList<double?> XValues { get; set; }

    /// <summary>
    /// Gets or sets the colour of the series.
    /// </summary>
    public string Color { get; set; }
}
