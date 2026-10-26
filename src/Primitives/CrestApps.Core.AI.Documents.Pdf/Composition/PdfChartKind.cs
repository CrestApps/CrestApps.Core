namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// The kinds of chart a PDF draws.
/// </summary>
internal enum PdfChartKind
{
    /// <summary>
    /// Upright bars, side by side per category.
    /// </summary>
    Column,

    /// <summary>
    /// Upright bars, stacked per category.
    /// </summary>
    StackedColumn,

    /// <summary>
    /// Sideways bars, side by side per category.
    /// </summary>
    Bar,

    /// <summary>
    /// Sideways bars, stacked per category.
    /// </summary>
    StackedBar,

    /// <summary>
    /// Lines through the values, with a marker on each.
    /// </summary>
    Line,

    /// <summary>
    /// Filled areas under the lines.
    /// </summary>
    Area,

    /// <summary>
    /// A pie of one series.
    /// </summary>
    Pie,

    /// <summary>
    /// A pie with its slices pulled apart.
    /// </summary>
    ExplodedPie,
}
