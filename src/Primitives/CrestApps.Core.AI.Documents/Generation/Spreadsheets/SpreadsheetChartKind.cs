namespace CrestApps.Core.AI.Documents.Generation.Spreadsheets;

/// <summary>
/// The kind of chart embedded in a generated spreadsheet.
/// </summary>
public enum SpreadsheetChartKind
{
    /// <summary>
    /// A vertical bar (column) chart.
    /// </summary>
    Column,

    /// <summary>
    /// A horizontal bar chart.
    /// </summary>
    Bar,

    /// <summary>
    /// A line chart.
    /// </summary>
    Line,

    /// <summary>
    /// A pie chart. Only the first value column is plotted.
    /// </summary>
    Pie,

    /// <summary>
    /// An area chart.
    /// </summary>
    Area,
}
