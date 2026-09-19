namespace CrestApps.Core.AI.Documents.Generation.Spreadsheets;

/// <summary>
/// Describes how a column's values are stored in the generated spreadsheet. Storing a value as a real
/// number or date rather than as text is what lets the spreadsheet application sum, sort, chart, and
/// format it; a number stored as text looks right but cannot be calculated on.
/// </summary>
public enum SpreadsheetDataKind
{
    /// <summary>
    /// The storage type is inferred from the column's number format and from the values themselves.
    /// </summary>
    Auto,

    /// <summary>
    /// Values are always written as text, even when they look numeric. Use this for identifiers such as
    /// zip codes, account numbers, and phone numbers whose leading zeros must be preserved.
    /// </summary>
    Text,

    /// <summary>
    /// Values are written as numbers.
    /// </summary>
    Number,

    /// <summary>
    /// Values are written as date serial numbers.
    /// </summary>
    Date,

    /// <summary>
    /// Values are written as boolean cells.
    /// </summary>
    Boolean,
}
