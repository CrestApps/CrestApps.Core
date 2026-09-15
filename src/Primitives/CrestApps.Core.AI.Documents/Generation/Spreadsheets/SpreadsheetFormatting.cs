namespace CrestApps.Core.AI.Documents.Generation.Spreadsheets;

/// <summary>
/// The presentation applied to a generated spreadsheet: per-column storage types and number formats,
/// header styling, conditional formatting, live formulas, a total row, and embedded charts.
/// <para>
/// Writers that cannot express presentation (CSV, plain text) ignore this; the spreadsheet writer
/// renders it. A <see langword="null"/> formatting spec still produces a valid workbook, with values
/// typed from the data itself rather than written as text.
/// </para>
/// </summary>
public sealed class SpreadsheetFormatting
{
    /// <summary>
    /// The default number of data rows plotted by a chart before it is truncated.
    /// </summary>
    public const int DefaultMaxChartCategories = 25;

    /// <summary>
    /// Gets or sets the worksheet name. Defaults to <c>Sheet1</c>.
    /// </summary>
    public string SheetName { get; set; }

    /// <summary>
    /// Gets or sets the per-column presentation.
    /// </summary>
    public IList<SpreadsheetColumnFormat> Columns { get; set; } = [];

    /// <summary>
    /// Gets or sets the style applied to the header row. Defaults to bold white text on a dark blue
    /// fill when <see cref="StyleHeader"/> is <see langword="true"/>.
    /// </summary>
    public SpreadsheetCellStyle HeaderStyle { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the header row is styled. Defaults to
    /// <see langword="true"/> when unset.
    /// <para>
    /// This and the other sheet-level switches are nullable so that "not mentioned" stays distinct from
    /// "explicitly turned off". A follow-up request that only changes a number format must not silently
    /// re-enable something the user had already turned off.
    /// </para>
    /// </summary>
    public bool? StyleHeader { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the header row stays visible while scrolling. Defaults
    /// to <see langword="true"/> when unset.
    /// </summary>
    public bool? FreezeHeader { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the header row carries filter dropdowns. Defaults to
    /// <see langword="true"/> when unset.
    /// </summary>
    public bool? AutoFilter { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether alternating data rows carry a light background fill.
    /// Defaults to <see langword="false"/> when unset.
    /// </summary>
    public bool? BandedRows { get; set; }

    /// <summary>
    /// Gets or sets the fill color used by <see cref="BandedRows"/>. Defaults to a light gray.
    /// </summary>
    public string BandColor { get; set; }

    /// <summary>
    /// Gets or sets the conditional formatting rules.
    /// </summary>
    public IList<SpreadsheetConditionalFormat> ConditionalFormats { get; set; } = [];

    /// <summary>
    /// Gets or sets the total row appended below the data.
    /// </summary>
    public SpreadsheetTotalRow TotalRow { get; set; }

    /// <summary>
    /// Gets or sets the charts embedded in the worksheet.
    /// </summary>
    public IList<SpreadsheetChart> Charts { get; set; } = [];

    /// <summary>
    /// Finds the format declared for a column, matching on the column name case-insensitively.
    /// </summary>
    /// <param name="columnName">The header name to look up.</param>
    /// <returns>The matching column format, or <see langword="null"/> when none is declared.</returns>
    public SpreadsheetColumnFormat FindColumn(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName) || Columns is null)
        {
            return null;
        }

        foreach (var column in Columns)
        {
            if (NameMatches(column?.Column, columnName))
            {
                return column;
            }
        }

        return null;
    }

    /// <summary>
    /// Determines whether two column names refer to the same column, ignoring case and surrounding
    /// whitespace. Models routinely echo a header back with different casing or padding, and a strict
    /// comparison would silently drop the format the user asked for.
    /// </summary>
    /// <param name="left">The first name.</param>
    /// <param name="right">The second name.</param>
    /// <returns><see langword="true"/> when the names match.</returns>
    public static bool NameMatches(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
