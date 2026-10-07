namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// A chart to put on a slide, or the parts of an existing chart to change. Unset properties are left as they
/// are when updating.
/// </summary>
public sealed class PresentationChartSpec
{
    /// <summary>
    /// The chart kinds the engine can write.
    /// </summary>
    public static readonly IReadOnlyList<string> Kinds =
    [
        "column",
        "stacked_column",
        "percent_column",
        "bar",
        "stacked_bar",
        "percent_bar",
        "line",
        "line_markers",
        "area",
        "stacked_area",
        "pie",
        "doughnut",
        "scatter",
        "radar",
    ];

    /// <summary>
    /// Gets or sets the chart kind, one of <see cref="Kinds"/>.
    /// </summary>
    public string Kind { get; set; }

    /// <summary>
    /// Gets or sets the chart title. An empty string removes it.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the category labels.
    /// </summary>
    public IList<string> Categories { get; set; }

    /// <summary>
    /// Gets or sets the data series.
    /// </summary>
    public IList<PresentationChartSeriesSpec> Series { get; set; }

    /// <summary>
    /// Gets or sets the title of the category axis. An empty string removes it.
    /// </summary>
    public string CategoryAxisTitle { get; set; }

    /// <summary>
    /// Gets or sets the title of the value axis. An empty string removes it.
    /// </summary>
    public string ValueAxisTitle { get; set; }

    /// <summary>
    /// Gets or sets how the chart looks, laid over the deck's recorded chart style.
    /// </summary>
    public PresentationChartStyle Style { get; set; }
}
