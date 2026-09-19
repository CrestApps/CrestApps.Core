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
