namespace CrestApps.Core.AI.Documents.Generation.Spreadsheets;

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
