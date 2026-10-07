namespace CrestApps.Core.AI.Documents.Word.Charts;

/// <summary>
/// Describes a chart: its type, its data and how it looks.
/// </summary>
internal sealed class WordChartSpec
{
    /// <summary>
    /// The chart types a chart can be written as.
    /// </summary>
    public static readonly IReadOnlyList<string> Types =
    [
        "column", "bar", "line", "pie", "doughnut", "area", "scatter",
        "stacked_column", "stacked_bar", "stacked_area", "percent_column", "percent_bar",
    ];

    /// <summary>
    /// Gets or sets the chart type, one of <see cref="Types"/>.
    /// </summary>
    public string Type { get; set; } = "column";

    /// <summary>
    /// Gets or sets the title, or <see langword="null"/> for none.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the category labels.
    /// </summary>
    public List<string> Labels { get; set; } = [];

    /// <summary>
    /// Gets or sets the data series.
    /// </summary>
    public List<WordChartSeries> Series { get; set; } = [];

    /// <summary>
    /// Gets or sets where the legend is: <c>right</c>, <c>left</c>, <c>top</c>, <c>bottom</c> or <c>none</c>.
    /// </summary>
    public string Legend { get; set; } = "bottom";

    /// <summary>
    /// Gets or sets a value indicating whether each value is labelled on the chart.
    /// </summary>
    public bool DataLabels { get; set; }

    /// <summary>
    /// Gets or sets the category axis title.
    /// </summary>
    public string XAxisTitle { get; set; }

    /// <summary>
    /// Gets or sets the value axis title.
    /// </summary>
    public string YAxisTitle { get; set; }

    /// <summary>
    /// Gets or sets the number format of the values, as a spreadsheet format code such as <c>#,##0</c> or <c>0%</c>.
    /// </summary>
    public string NumberFormat { get; set; }

    /// <summary>
    /// Gets or sets the series colors, in order.
    /// </summary>
    public List<string> Colors { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether lines are smoothed.
    /// </summary>
    public bool Smooth { get; set; }

    /// <summary>
    /// Gets the base kind: <c>bar</c> (columns or bars), <c>line</c>, <c>pie</c>, <c>doughnut</c>, <c>area</c> or <c>scatter</c>.
    /// </summary>
    public string Kind => Normalize(Type) switch
    {
        "column" or "bar" or "stacked_column" or "stacked_bar" or "percent_column" or "percent_bar" => "bar",
        "stacked_area" => "area",
        var other => other,
    };

    /// <summary>
    /// Gets a value indicating whether bars run horizontally.
    /// </summary>
    public bool IsHorizontal => Normalize(Type) is "bar" or "stacked_bar" or "percent_bar";

    /// <summary>
    /// Gets how series are grouped: <c>clustered</c>, <c>stacked</c> or <c>percentStacked</c>.
    /// </summary>
    public string Grouping => Normalize(Type) switch
    {
        "stacked_column" or "stacked_bar" or "stacked_area" => "stacked",
        "percent_column" or "percent_bar" => "percentStacked",
        _ => Kind is "line" or "area" ? "standard" : "clustered",
    };

    /// <summary>
    /// Normalizes a chart type a model wrote, such as <c>Column</c>, <c>stacked bar</c> or <c>donut</c>.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>One of <see cref="Types"/>, or <c>column</c> when the type is unknown.</returns>
    public static string Normalize(string type)
    {
        var text = (type ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');

        return text switch
        {
            "" or "columns" or "vertical_bar" or "clustered_column" => "column",
            "bars" or "horizontal_bar" or "clustered_bar" => "bar",
            "lines" or "line_chart" => "line",
            "pie_chart" => "pie",
            "donut" or "ring" => "doughnut",
            "xy" or "scatter_plot" => "scatter",
            "stacked" => "stacked_column",
            "percent" or "100%_column" or "percent_stacked_column" => "percent_column",
            "100%_bar" or "percent_stacked_bar" => "percent_bar",
            _ => Types.Contains(text, StringComparer.Ordinal) ? text : "column",
        };
    }
}

/// <summary>
/// One data series of a chart.
/// </summary>
internal sealed class WordChartSeries
{
    /// <summary>
    /// Gets or sets the series name, shown in the legend.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the values, one per category; a missing value leaves a gap.
    /// </summary>
    public List<double?> Values { get; set; } = [];

    /// <summary>
    /// Gets or sets the horizontal values of a scatter chart.
    /// </summary>
    public List<double?> XValues { get; set; } = [];

    /// <summary>
    /// Gets or sets the series color, or <see langword="null"/> for the palette's.
    /// </summary>
    public string Color { get; set; }
}
