namespace CrestApps.Core.AI.Documents.Generation.Spreadsheets;

/// <summary>
/// A total row appended below the data, built from live spreadsheet formulas rather than pre-computed
/// text so it recalculates when the reader edits or filters the sheet.
/// </summary>
public sealed class SpreadsheetTotalRow
{
    /// <summary>
    /// Gets or sets the label placed in the first column of the total row, for example <c>Total</c>.
    /// </summary>
    public string Label { get; set; }

    /// <summary>
    /// Gets or sets the style applied to the total row. Defaults to bold with a top border.
    /// </summary>
    public SpreadsheetCellStyle Style { get; set; }

    /// <summary>
    /// Gets or sets the aggregates placed in the total row.
    /// </summary>
    public IList<SpreadsheetTotalColumn> Columns { get; set; } = [];
}

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
