namespace CrestApps.Core.AI.Models;

/// <summary>
/// The kinds of chart that can be told apart from the drawing itself.
/// </summary>
/// <remarks>
/// A chart is only typed when the way it was drawn says what it is. Values printed on a plot say what the
/// numbers are and nothing about how they were carried, so a chart read that way carries no type at all. A
/// missing type costs a reader one detail; an invented one tells them something untrue about the document.
/// </remarks>
public static class ChartTypes
{
    /// <summary>
    /// The values were followed along the sloped polylines the chart draws, which is what a line chart is
    /// made of. A bar or column chart draws nothing sloped to follow, so it never reads as one.
    /// </summary>
    public const string Line = "Line";
}
