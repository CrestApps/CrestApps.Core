namespace CrestApps.Core.AI.Models;

/// <summary>
/// One named series of a chart.
/// </summary>
public sealed class ChartSeries
{
    /// <summary>
    /// Gets or sets the series name, as printed in the legend.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the points, each an x and a y. Populated only when the values came from the document.
    /// </summary>
    public IList<double[]> Points { get; set; } = [];
}
