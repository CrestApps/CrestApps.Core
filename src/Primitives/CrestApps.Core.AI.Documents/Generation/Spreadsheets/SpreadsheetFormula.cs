using System.Globalization;
using System.Text;

namespace CrestApps.Core.AI.Documents.Generation.Spreadsheets;

/// <summary>
/// Builds spreadsheet cell references and resolves the column-name placeholders used in formulas.
/// <para>
/// A model writing a formula knows the column names it asked for but not where those columns landed in
/// the generated sheet, and the layout shifts as soon as a computed column is appended. Writing
/// <c>={Revenue}-{Cost}</c> and letting the writer resolve the references keeps the formula correct no
/// matter how the sheet is laid out.
/// </para>
/// </summary>
public static class SpreadsheetFormula
{
    /// <summary>
    /// Converts a zero-based column index into its spreadsheet column name (<c>0</c> becomes <c>A</c>,
    /// <c>26</c> becomes <c>AA</c>).
    /// </summary>
    /// <param name="index">The zero-based column index.</param>
    /// <returns>The column name.</returns>
    public static string ToColumnName(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        Span<char> buffer = stackalloc char[8];
        var position = buffer.Length;
        var remaining = index;

        do
        {
            buffer[--position] = (char)('A' + (remaining % 26));
            remaining = (remaining / 26) - 1;
        }
        while (remaining >= 0);

        return new string(buffer[position..]);
    }

    /// <summary>
    /// Builds an A1-style cell reference.
    /// </summary>
    /// <param name="columnIndex">The zero-based column index.</param>
    /// <param name="rowNumber">The one-based row number.</param>
    /// <returns>The cell reference, for example <c>B7</c>.</returns>
    public static string ToCellReference(int columnIndex, int rowNumber)
    {
        return ToColumnName(columnIndex) + rowNumber.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Builds an A1-style range reference spanning one column.
    /// </summary>
    /// <param name="columnIndex">The zero-based column index.</param>
    /// <param name="firstRowNumber">The one-based first row.</param>
    /// <param name="lastRowNumber">The one-based last row.</param>
    /// <returns>The range reference, for example <c>B2:B51</c>.</returns>
    public static string ToColumnRange(int columnIndex, int firstRowNumber, int lastRowNumber)
    {
        var name = ToColumnName(columnIndex);

        return $"{name}{firstRowNumber.ToString(CultureInfo.InvariantCulture)}:{name}{lastRowNumber.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Resolves the <c>{Column Name}</c> and <c>{row}</c> placeholders in a formula against the
    /// generated sheet's layout.
    /// </summary>
    /// <param name="formula">The formula, with or without a leading <c>=</c>.</param>
    /// <param name="rowNumber">The one-based row the formula is written to.</param>
    /// <param name="columnResolver">
    /// Resolves a placeholder name to a zero-based column index, returning <see langword="false"/> when
    /// the name is not a column in the sheet.
    /// </param>
    /// <returns>
    /// The resolved formula without its leading <c>=</c>, or <see langword="null"/> when the formula is
    /// blank or references a column that does not exist. A formula that cannot be resolved is dropped
    /// rather than written, because a broken reference makes the whole workbook open with an error.
    /// </returns>
    public static string Resolve(string formula, int rowNumber, TryResolveColumn columnResolver)
    {
        ArgumentNullException.ThrowIfNull(columnResolver);

        if (string.IsNullOrWhiteSpace(formula))
        {
            return null;
        }

        var text = formula.Trim();

        if (text.StartsWith('='))
        {
            text = text[1..];
        }

        if (text.Length == 0)
        {
            return null;
        }

        if (!text.Contains('{', StringComparison.Ordinal))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length + 16);
        var index = 0;

        while (index < text.Length)
        {
            var open = text.IndexOf('{', index);

            if (open < 0)
            {
                builder.Append(text, index, text.Length - index);

                break;
            }

            var close = text.IndexOf('}', open + 1);

            if (close < 0)
            {
                builder.Append(text, index, text.Length - index);

                break;
            }

            builder.Append(text, index, open - index);

            var name = text[(open + 1)..close].Trim();

            if (string.Equals(name, "row", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(rowNumber.ToString(CultureInfo.InvariantCulture));
            }
            else if (columnResolver(name, out var columnIndex))
            {
                builder.Append(ToCellReference(columnIndex, rowNumber));
            }
            else
            {
                return null;
            }

            index = close + 1;
        }

        return builder.Length == 0
            ? null
            : builder.ToString();
    }

    /// <summary>
    /// Resolves a placeholder name to a zero-based column index.
    /// </summary>
    /// <param name="name">The column name from the placeholder.</param>
    /// <param name="columnIndex">The resolved zero-based column index.</param>
    /// <returns><see langword="true"/> when the name matched a column.</returns>
    public delegate bool TryResolveColumn(string name, out int columnIndex);

    /// <summary>
    /// Builds the aggregate formula for a total row. <c>SUBTOTAL</c> is used rather than <c>SUM</c> so
    /// the total follows the reader's filtering instead of silently reporting the unfiltered figure.
    /// </summary>
    /// <param name="function">The aggregate to apply.</param>
    /// <param name="range">The range to aggregate.</param>
    /// <returns>The formula, without its leading <c>=</c>.</returns>
    public static string ToAggregate(SpreadsheetAggregateFunction function, string range)
    {
        var code = function switch
        {
            SpreadsheetAggregateFunction.Average => 101,
            SpreadsheetAggregateFunction.Count => 103,
            SpreadsheetAggregateFunction.Max => 104,
            SpreadsheetAggregateFunction.Min => 105,
            _ => 109,
        };

        return $"SUBTOTAL({code.ToString(CultureInfo.InvariantCulture)},{range})";
    }
}
