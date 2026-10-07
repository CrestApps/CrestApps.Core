namespace CrestApps.Core.AI.Documents.Presentations.Models;

/// <summary>
/// A chart on a slide, read from the values the chart caches so it can be understood and drawn without
/// opening its workbook.
/// </summary>
public sealed class PresentationChart
{
    /// <summary>
    /// Gets or sets the chart kind: <c>column</c>, <c>bar</c>, <c>line</c>, <c>pie</c>, <c>doughnut</c>,
    /// <c>area</c>, <c>scatter</c>, <c>radar</c>, <c>bubble</c> or <c>other</c>.
    /// </summary>
    public string Kind { get; set; } = "column";

    /// <summary>
    /// Gets or sets a value indicating whether the series are stacked on each other.
    /// </summary>
    public bool Stacked { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether stacked series are scaled to fill 100 percent.
    /// </summary>
    public bool PercentStacked { get; set; }

    /// <summary>
    /// Gets or sets the chart title.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the category labels.
    /// </summary>
    public IList<string> Categories { get; set; } = [];

    /// <summary>
    /// Gets or sets the data series.
    /// </summary>
    public IList<PresentationChartSeries> Series { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether a legend is shown.
    /// </summary>
    public bool ShowLegend { get; set; }

    /// <summary>
    /// Gets or sets where the legend sits: <c>bottom</c>, <c>top</c>, <c>left</c> or <c>right</c>.
    /// </summary>
    public string LegendPosition { get; set; } = "bottom";

    /// <summary>
    /// Gets or sets a value indicating whether each point is labelled with its value.
    /// </summary>
    public bool ShowDataLabels { get; set; }

    /// <summary>
    /// Gets or sets the number format applied to values, for example <c>#,##0</c> or <c>0%</c>.
    /// </summary>
    public string NumberFormat { get; set; }

    /// <summary>
    /// Gets or sets the title of the category axis.
    /// </summary>
    public string CategoryAxisTitle { get; set; }

    /// <summary>
    /// Gets or sets the title of the value axis.
    /// </summary>
    public string ValueAxisTitle { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the chart carries a workbook PowerPoint can open to edit the
    /// data.
    /// </summary>
    public bool HasEmbeddedWorkbook { get; set; }

    /// <summary>
    /// Gets or sets the typeface of the chart text, when the chart sets one.
    /// </summary>
    public string Font { get; set; }

    /// <summary>
    /// Gets or sets the size of the chart text in points, when the chart sets one.
    /// </summary>
    public double? FontSize { get; set; }

    /// <summary>
    /// Gets or sets the colour of the chart text, when the chart sets one.
    /// </summary>
    public string TextColor { get; set; }
}
