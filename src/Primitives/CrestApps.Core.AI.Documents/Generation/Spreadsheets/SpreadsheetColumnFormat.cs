namespace CrestApps.Core.AI.Documents.Generation.Spreadsheets;

/// <summary>
/// The presentation applied to a single column of a generated spreadsheet: how its values are stored,
/// how they are formatted, how wide the column is, and how the cells look.
/// </summary>
public sealed class SpreadsheetColumnFormat
{
    /// <summary>
    /// Gets or sets the column this format applies to, matched against the generated header row. The
    /// match is case-insensitive and ignores surrounding whitespace. When the name does not match an
    /// existing header and <see cref="Formula"/> is set, the column is appended as a new computed
    /// column instead.
    /// </summary>
    public string Column { get; set; }

    /// <summary>
    /// Gets or sets how the column's values are stored. Defaults to <see cref="SpreadsheetDataKind.Auto"/>,
    /// which derives the storage type from <see cref="NumberFormat"/> and from the values themselves.
    /// </summary>
    public SpreadsheetDataKind DataKind { get; set; }

    /// <summary>
    /// Gets or sets the built-in number presentation for the column.
    /// </summary>
    public SpreadsheetNumberFormat NumberFormat { get; set; }

    /// <summary>
    /// Gets or sets an explicit spreadsheet number format code, for example <c>#,##0.00_);[Red](#,##0.00)</c>.
    /// When set, it overrides <see cref="NumberFormat"/>, <see cref="Decimals"/>, and <see cref="CurrencySymbol"/>.
    /// </summary>
    public string FormatCode { get; set; }

    /// <summary>
    /// Gets or sets the number of decimal places used by the numeric formats. Defaults to two for
    /// currency and accounting, and to zero otherwise.
    /// </summary>
    public int? Decimals { get; set; }

    /// <summary>
    /// Gets or sets the currency symbol used by <see cref="SpreadsheetNumberFormat.Currency"/> and
    /// <see cref="SpreadsheetNumberFormat.Accounting"/>. Defaults to <c>$</c>.
    /// </summary>
    public string CurrencySymbol { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether negative values render in red and in parentheses.
    /// </summary>
    public bool NegativesInRed { get; set; }

    /// <summary>
    /// Gets or sets the column width in character units. When left unset the width is measured from the
    /// column's content so the values are not rendered as <c>####</c>.
    /// </summary>
    public double? Width { get; set; }

    /// <summary>
    /// Gets or sets the style applied to the column's data cells.
    /// </summary>
    public SpreadsheetCellStyle Style { get; set; }

    /// <summary>
    /// Gets or sets a formula written into every data cell of the column, making the generated workbook
    /// recalculate live when the reader edits it. Reference other columns by name in braces
    /// (<c>={Revenue}-{Cost}</c>); each placeholder resolves to that column's cell on the same row. Use
    /// <c>{row}</c> for the current row number. Plain cell references such as <c>=B2*C2</c> also work.
    /// When <see cref="Column"/> does not match an existing header, the column is appended to the sheet.
    /// </summary>
    public string Formula { get; set; }
}
