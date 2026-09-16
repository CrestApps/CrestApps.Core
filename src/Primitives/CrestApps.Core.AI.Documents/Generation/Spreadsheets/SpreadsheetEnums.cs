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

/// <summary>
/// Horizontal alignment applied to a cell.
/// </summary>
public enum SpreadsheetHorizontalAlignment
{
    /// <summary>
    /// The alignment the spreadsheet application chooses by default.
    /// </summary>
    General,

    /// <summary>
    /// Left aligned.
    /// </summary>
    Left,

    /// <summary>
    /// Center aligned.
    /// </summary>
    Center,

    /// <summary>
    /// Right aligned.
    /// </summary>
    Right,
}

/// <summary>
/// The kind of conditional formatting rule applied to a column.
/// </summary>
public enum SpreadsheetConditionalRule
{
    /// <summary>
    /// Highlights cells greater than <see cref="SpreadsheetConditionalFormat.Value"/>.
    /// </summary>
    GreaterThan,

    /// <summary>
    /// Highlights cells less than <see cref="SpreadsheetConditionalFormat.Value"/>.
    /// </summary>
    LessThan,

    /// <summary>
    /// Highlights cells equal to <see cref="SpreadsheetConditionalFormat.Value"/>.
    /// </summary>
    EqualTo,

    /// <summary>
    /// Highlights cells between <see cref="SpreadsheetConditionalFormat.Value"/> and
    /// <see cref="SpreadsheetConditionalFormat.SecondValue"/>.
    /// </summary>
    Between,

    /// <summary>
    /// Highlights cells whose text contains <see cref="SpreadsheetConditionalFormat.Value"/>.
    /// </summary>
    ContainsText,

    /// <summary>
    /// Highlights values that appear more than once in the column.
    /// </summary>
    DuplicateValues,

    /// <summary>
    /// Applies a two- or three-color gradient across the column's value range.
    /// </summary>
    ColorScale,

    /// <summary>
    /// Draws an in-cell data bar proportional to each value.
    /// </summary>
    DataBar,

    /// <summary>
    /// Draws an in-cell icon set based on where each value falls in the column's range.
    /// </summary>
    IconSet,
}

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

/// <summary>
/// The kind of chart embedded in a generated spreadsheet.
/// </summary>
public enum SpreadsheetChartKind
{
    /// <summary>
    /// A vertical bar (column) chart.
    /// </summary>
    Column,

    /// <summary>
    /// A horizontal bar chart.
    /// </summary>
    Bar,

    /// <summary>
    /// A line chart.
    /// </summary>
    Line,

    /// <summary>
    /// A pie chart. Only the first value column is plotted.
    /// </summary>
    Pie,

    /// <summary>
    /// An area chart.
    /// </summary>
    Area,
}
