namespace CrestApps.Core.AI.Documents.Presentations.Models;

/// <summary>
/// One data series of a chart.
/// </summary>
public sealed class PresentationChartSeries
{
    /// <summary>
    /// Gets or sets the series name shown in the legend.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the values, one per category; a missing point is <see langword="null"/>.
    /// </summary>
    public IList<double?> Values { get; set; } = [];

    /// <summary>
    /// Gets or sets the horizontal values of a scatter series.
    /// </summary>
    public IList<double?> XValues { get; set; } = [];

    /// <summary>
    /// Gets or sets the colour the series is drawn in, as six hexadecimal digits, when the chart sets one.
    /// </summary>
    public string Color { get; set; }

    /// <summary>
    /// Gets or sets per-point colours, which a pie or doughnut uses for its slices.
    /// </summary>
    public IList<string> PointColors { get; set; } = [];
}
