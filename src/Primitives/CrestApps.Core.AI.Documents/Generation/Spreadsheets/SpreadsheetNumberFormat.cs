namespace CrestApps.Core.AI.Documents.Generation.Spreadsheets;

/// <summary>
/// The built-in number presentations a column can use. Each maps to a spreadsheet number format code,
/// refined by the decimal count and currency symbol on the column format.
/// </summary>
public enum SpreadsheetNumberFormat
{
    /// <summary>
    /// No explicit number format; the spreadsheet application decides how to present the value.
    /// </summary>
    General,

    /// <summary>
    /// Plain text.
    /// </summary>
    Text,

    /// <summary>
    /// A number with thousands separators and a fixed number of decimals.
    /// </summary>
    Number,

    /// <summary>
    /// A currency amount, for example <c>$1,234.56</c>.
    /// </summary>
    Currency,

    /// <summary>
    /// An accounting-style amount with aligned currency symbols and parenthesized negatives.
    /// </summary>
    Accounting,

    /// <summary>
    /// A percentage. Values are expected to be stored as fractions, so <c>0.15</c> renders as <c>15%</c>.
    /// </summary>
    Percent,

    /// <summary>
    /// Scientific notation.
    /// </summary>
    Scientific,

    /// <summary>
    /// A short date, for example <c>2026-09-15</c>.
    /// </summary>
    Date,

    /// <summary>
    /// A date and time.
    /// </summary>
    DateTime,

    /// <summary>
    /// A time of day.
    /// </summary>
    Time,

    /// <summary>
    /// A duration in hours and minutes that is allowed to exceed 24 hours.
    /// </summary>
    Duration,
}
