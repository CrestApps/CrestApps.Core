namespace CrestApps.Core.AI.Models;

/// <summary>
/// How far the values read off a chart can be trusted.
/// </summary>
/// <remarks>
/// This distinction is the difference between a useful answer and a confidently wrong one. A number read off
/// a raster image by eye looks exactly like a number lifted from the file's own geometry, and only one of
/// them is true.
/// </remarks>
public static class ChartValueConfidence
{
    /// <summary>
    /// The values came from the document itself: vector geometry, or data labels printed on the chart.
    /// </summary>
    public const string Exact = "Exact";

    /// <summary>
    /// Only the axes and the legend could be read. The series values were not printed.
    /// </summary>
    public const string AxesOnly = "AxesOnly";

    /// <summary>
    /// Only the shape and the trend are known. No value here is machine-readable, and none is stored.
    /// </summary>
    public const string Descriptive = "Descriptive";
}
