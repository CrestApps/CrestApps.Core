namespace CrestApps.Core.AI.Documents.Generation.Spreadsheets;

/// <summary>
/// The aggregate applied to a column in a generated total row.
/// </summary>
public enum SpreadsheetAggregateFunction
{
    /// <summary>
    /// Sums the column.
    /// </summary>
    Sum,

    /// <summary>
    /// Averages the column.
    /// </summary>
    Average,

    /// <summary>
    /// Counts the populated cells in the column.
    /// </summary>
    Count,

    /// <summary>
    /// Takes the smallest value in the column.
    /// </summary>
    Min,

    /// <summary>
    /// Takes the largest value in the column.
    /// </summary>
    Max,
}
