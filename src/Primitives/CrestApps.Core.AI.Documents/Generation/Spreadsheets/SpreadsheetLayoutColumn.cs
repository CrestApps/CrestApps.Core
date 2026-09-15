namespace CrestApps.Core.AI.Documents.Generation.Spreadsheets;

/// <summary>
/// One fully resolved column of a generated spreadsheet: where it sits, how its values are stored, how
/// they are formatted, and how wide the column is. Every ambiguity in the requested formatting has
/// already been settled by <see cref="SpreadsheetLayout"/>.
/// </summary>
public sealed class SpreadsheetLayoutColumn
{
    /// <summary>
    /// Gets the header text.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// Gets the zero-based position of the column in the generated sheet.
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// Gets how the column's values are stored. This is always a concrete kind; the
    /// <see cref="SpreadsheetDataKind.Auto"/> placeholder has already been resolved against the data.
    /// </summary>
    public SpreadsheetDataKind Kind { get; init; }

    /// <summary>
    /// Gets the resolved number format code, or <see langword="null"/> when the column carries no
    /// explicit format.
    /// </summary>
    public string NumberFormatCode { get; init; }

    /// <summary>
    /// Gets the column width in character units.
    /// </summary>
    public double Width { get; init; }

    /// <summary>
    /// Gets the style applied to the column's data cells, or <see langword="null"/> when unstyled.
    /// </summary>
    public SpreadsheetCellStyle Style { get; init; }

    /// <summary>
    /// Gets the unresolved formula template written into each data cell, or <see langword="null"/> when
    /// the column holds literal values.
    /// </summary>
    public string Formula { get; init; }

    /// <summary>
    /// Gets a value indicating whether this column was appended by the formatting spec rather than
    /// present in the source data. A computed column has no source values, only a formula.
    /// </summary>
    public bool IsComputed { get; init; }

    /// <summary>
    /// Gets the requested format this column was resolved from, or <see langword="null"/> when none was
    /// declared.
    /// </summary>
    public SpreadsheetColumnFormat Format { get; init; }
}
