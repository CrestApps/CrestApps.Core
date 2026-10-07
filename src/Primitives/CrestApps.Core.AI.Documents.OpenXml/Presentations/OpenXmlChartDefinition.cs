namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// Everything needed to write a chart part: its kind, data and look, already resolved from the request and
/// the deck's recorded chart style.
/// </summary>
internal sealed class OpenXmlChartDefinition
{
    /// <summary>
    /// Gets or sets the plot: <c>bar</c>, <c>line</c>, <c>area</c>, <c>pie</c>, <c>doughnut</c>,
    /// <c>scatter</c> or <c>radar</c>.
    /// </summary>
    public string Plot { get; set; } = "bar";

    /// <summary>
    /// Gets or sets a value indicating whether bars run horizontally.
    /// </summary>
    public bool Horizontal { get; set; }

    /// <summary>
    /// Gets or sets the grouping: <c>clustered</c>, <c>standard</c>, <c>stacked</c> or <c>percentStacked</c>.
    /// </summary>
    public string Grouping { get; set; } = "clustered";

    /// <summary>
    /// Gets or sets a value indicating whether line points are marked.
    /// </summary>
    public bool Markers { get; set; }

    /// <summary>
    /// Gets or sets the title, or <see langword="null"/> for none.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the category labels.
    /// </summary>
    public List<string> Categories { get; set; } = [];

    /// <summary>
    /// Gets or sets the series.
    /// </summary>
    public List<OpenXmlChartSeriesDefinition> Series { get; set; } = [];

    /// <summary>
    /// Gets or sets the legend position (<c>b</c>, <c>t</c>, <c>l</c>, <c>r</c>), or <see langword="null"/>
    /// for no legend.
    /// </summary>
    public string LegendPosition { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether points are labelled with their values.
    /// </summary>
    public bool DataLabels { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether value gridlines are drawn.
    /// </summary>
    public bool Gridlines { get; set; } = true;

    /// <summary>
    /// Gets or sets the number format of the values.
    /// </summary>
    public string NumberFormat { get; set; }

    /// <summary>
    /// Gets or sets the typeface of the chart text.
    /// </summary>
    public string Font { get; set; }

    /// <summary>
    /// Gets or sets the size of the chart text in points.
    /// </summary>
    public double FontSize { get; set; } = 12;

    /// <summary>
    /// Gets or sets the colour of the chart text, or <see langword="null"/> for the theme's muted text colour.
    /// </summary>
    public string TextColor { get; set; }

    /// <summary>
    /// Gets or sets the category axis title.
    /// </summary>
    public string CategoryAxisTitle { get; set; }

    /// <summary>
    /// Gets or sets the value axis title.
    /// </summary>
    public string ValueAxisTitle { get; set; }

    /// <summary>
    /// Gets or sets the palette series are coloured from, in order, when a series names no colour of its own.
    /// </summary>
    public List<string> Palette { get; set; } = [];
}
