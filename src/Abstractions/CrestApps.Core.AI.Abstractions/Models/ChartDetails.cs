namespace CrestApps.Core.AI.Models;

/// <summary>
/// The detail a chart carries beyond what a figure carries.
/// </summary>
public sealed class ChartDetails
{
    /// <summary>
    /// Gets or sets the kind of chart, such as a bar, line or scatter chart.
    /// </summary>
    public string ChartType { get; set; }

    /// <summary>
    /// Gets or sets how far the series values can be trusted. See <see cref="ChartValueConfidence"/>.
    /// </summary>
    public string ValueConfidence { get; set; } = ChartValueConfidence.Descriptive;

    /// <summary>
    /// Gets or sets the series. A chart whose confidence is descriptive carries none: an estimate is never
    /// stored as a value.
    /// </summary>
    public IList<ChartSeries> Series { get; set; } = [];

    /// <summary>
    /// Gets or sets the horizontal axis title, including its units.
    /// </summary>
    public string AxisX { get; set; }

    /// <summary>
    /// Gets or sets the vertical axis title, including its units.
    /// </summary>
    public string AxisY { get; set; }
}
