using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Ingestion.Pdf.Services;

/// <summary>
/// What could be read off a chart, and how far it can be trusted.
/// </summary>
internal sealed class ChartExtraction
{
    /// <summary>
    /// Gets or sets how far the values can be trusted. See <see cref="ChartValueConfidence"/>.
    /// </summary>
    public string ValueConfidence { get; set; } = ChartValueConfidence.Descriptive;

    /// <summary>
    /// Gets or sets the series, in the chart's own units.
    /// </summary>
    public IReadOnlyList<ChartSeries> Series { get; set; } = [];

    /// <summary>
    /// Gets or sets the kind of chart, when the drawing says what kind it is, or <see langword="null"/> when
    /// it does not. See <see cref="ChartTypes"/>.
    /// </summary>
    public string ChartType { get; set; }

    /// <summary>
    /// Gets or sets the horizontal axis title, as the chart prints it.
    /// </summary>
    public string AxisX { get; set; }

    /// <summary>
    /// Gets or sets the vertical axis title, as the chart prints it.
    /// </summary>
    public string AxisY { get; set; }
}
