namespace CrestApps.Core.AI.Documents.Generation.Spreadsheets;

/// <summary>
/// A chart embedded in the generated worksheet. The chart is bound to the worksheet's cell ranges, so
/// it redraws itself when the reader edits the underlying data.
/// </summary>
public sealed class SpreadsheetChart
{
    /// <summary>
    /// Gets or sets the chart kind.
    /// </summary>
    public SpreadsheetChartKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the chart title.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the column supplying the category (axis) labels, matched against the generated
    /// header row.
    /// </summary>
    public string CategoryColumn { get; set; }

    /// <summary>
    /// Gets or sets the columns plotted as series, matched against the generated header row.
    /// </summary>
    public IList<string> ValueColumns { get; set; } = [];

    /// <summary>
    /// Gets or sets the maximum number of data rows plotted. Charts become unreadable past a few dozen
    /// categories, so the rows are limited and the chart title notes the limit. Defaults to 25.
    /// </summary>
    public int? MaxCategories { get; set; }

    /// <summary>
    /// Gets or sets the chart width in pixels. Defaults to 720.
    /// </summary>
    public int? Width { get; set; }

    /// <summary>
    /// Gets or sets the chart height in pixels. Defaults to 400.
    /// </summary>
    public int? Height { get; set; }
}
