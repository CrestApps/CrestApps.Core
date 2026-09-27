namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// A chart drawn as vector graphics in the flow of a composed document.
/// </summary>
internal sealed class PdfChartDefinition
{
    /// <summary>
    /// Gets or sets the chart type: <c>column</c>, <c>bar</c>, <c>stacked_column</c>,
    /// <c>stacked_bar</c>, <c>line</c>, <c>area</c> or <c>pie</c>.
    /// </summary>
    public string ChartType { get; set; }

    /// <summary>
    /// Gets or sets the title printed above the chart.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the category labels along the axis.
    /// </summary>
    public List<string> Labels { get; set; } = [];

    /// <summary>
    /// Gets or sets the series plotted against the labels.
    /// </summary>
    public List<PdfChartSeriesDefinition> Series { get; set; } = [];

    /// <summary>
    /// Gets or sets the title of the category axis.
    /// </summary>
    public string XAxisTitle { get; set; }

    /// <summary>
    /// Gets or sets the title of the value axis.
    /// </summary>
    public string YAxisTitle { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a legend is drawn. Defaults to drawing one when there is more
    /// than one series, and always for a pie.
    /// </summary>
    public bool? Legend { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether each point is labelled with its value.
    /// </summary>
    public bool? DataLabels { get; set; }

    /// <summary>
    /// Gets or sets the number format of the value axis and data labels, for example <c>#,##0</c> or
    /// <c>0%</c>.
    /// </summary>
    public string NumberFormat { get; set; }

    /// <summary>
    /// Gets or sets the chart width as a percentage of the text width.
    /// </summary>
    public double? WidthPercent { get; set; }

    /// <summary>
    /// Gets or sets the chart height in points.
    /// </summary>
    public double? Height { get; set; }

    /// <summary>
    /// Gets or sets a caption printed under the chart.
    /// </summary>
    public string Caption { get; set; }
}
