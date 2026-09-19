namespace CrestApps.Core.AI.Documents.Generation.Spreadsheets;

/// <summary>
/// One aggregated column within a <see cref="SpreadsheetTotalRow"/>.
/// </summary>
public sealed class SpreadsheetTotalColumn
{
    /// <summary>
    /// Gets or sets the column to aggregate, matched against the generated header row.
    /// </summary>
    public string Column { get; set; }

    /// <summary>
    /// Gets or sets the aggregate applied to the column.
    /// </summary>
    public SpreadsheetAggregateFunction Function { get; set; }
}
