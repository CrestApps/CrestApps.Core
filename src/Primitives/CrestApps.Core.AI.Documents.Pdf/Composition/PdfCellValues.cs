using System.Globalization;
using CrestApps.Core.AI.Documents.Generation.Spreadsheets;
using CrestApps.Core.AI.Documents.Tabular;

namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// Reads and presents table cell values the way a spreadsheet presents them.
/// </summary>
/// <remarks>
/// A table's cells arrive as text — from a tabular export, a query, or a model — and a column formatted as
/// currency has to print <c>$74,612.16</c> whether the cell said <c>74612.16</c> or <c>$74,612.16</c>. The
/// presentation itself is the one the tabular preview and the spreadsheet export share, so a report reads the
/// same in every format it is delivered in.
/// </remarks>
internal static class PdfCellValues
{
    /// <summary>
    /// Builds the spreadsheet format a column's presentation settings describe.
    /// </summary>
    /// <param name="column">The column.</param>
    /// <returns>The format, or <see langword="null"/> when the column is presented as it is written.</returns>
    public static SpreadsheetColumnFormat ToSpreadsheetFormat(PdfTableColumnDefinition column)
    {
        if (column is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(column.FormatCode))
        {
            return new SpreadsheetColumnFormat
            {
                Column = column.Header,
                FormatCode = column.FormatCode,
                NegativesInRed = column.NegativesInRed == true,
            };
        }

        if (string.IsNullOrWhiteSpace(column.Format))
        {
            return null;
        }

        var name = column.Format.Trim().ToLowerInvariant();
        var decimals = column.Decimals;

        if (name is "integer" or "int" or "whole")
        {
            name = "number";
            decimals ??= 0;
        }

        if (name is "money" or "dollar" or "dollars")
        {
            name = "currency";
        }

        if (name is "percentage")
        {
            name = "percent";
        }

        if (!Enum.TryParse<SpreadsheetNumberFormat>(name, ignoreCase: true, out var format) ||
            format is SpreadsheetNumberFormat.General or SpreadsheetNumberFormat.Text)
        {
            return null;
        }

        return new SpreadsheetColumnFormat
        {
            Column = column.Header,
            NumberFormat = format,
            Decimals = decimals,
            CurrencySymbol = column.CurrencySymbol,
            NegativesInRed = column.NegativesInRed == true,
        };
    }

    /// <summary>
    /// Presents a cell.
    /// </summary>
    /// <param name="value">The cell as written.</param>
    /// <param name="format">The column's format, or <see langword="null"/>.</param>
    /// <returns>The text to print.</returns>
    public static string Format(string value, SpreadsheetColumnFormat format)
    {
        if (string.IsNullOrEmpty(value) || format is null)
        {
            return value ?? string.Empty;
        }

        var isPercent = format.NumberFormat == SpreadsheetNumberFormat.Percent ||
            (format.FormatCode?.Contains('%', StringComparison.Ordinal) ?? false);

        if (TryParseNumber(value, isPercent, out var number))
        {
            return TabularPreviewValueFormat.Format(number, format);
        }

        return TabularPreviewValueFormat.Format(value, format);
    }

    /// <summary>
    /// Reads a number from a cell, tolerating currency symbols, grouping, a trailing percent sign and
    /// accounting brackets.
    /// </summary>
    /// <param name="value">The cell as written.</param>
    /// <param name="percentAsFraction">Whether <c>15%</c> reads as <c>0.15</c>.</param>
    /// <param name="number">The number.</param>
    /// <returns><see langword="true"/> when the cell holds a number.</returns>
    public static bool TryParseNumber(string value, bool percentAsFraction, out double number)
    {
        number = 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return true;
        }

        var negative = false;

        if (text.Length > 2 && text[0] == '(' && text[^1] == ')')
        {
            negative = true;
            text = text[1..^1].Trim();
        }

        var percent = false;

        if (text.EndsWith('%'))
        {
            percent = true;
            text = text[..^1].Trim();
        }

        var builder = new System.Text.StringBuilder(text.Length);

        foreach (var character in text)
        {
            if (char.IsAsciiDigit(character) || character is '.' or '-' or '+' or 'e' or 'E')
            {
                builder.Append(character);
            }
            else if (character is ',' or ' ' or ' ' or '$' or '€' or '£' or '¥' or '₹')
            {
                continue;
            }
            else
            {
                return false;
            }
        }

        if (builder.Length == 0 ||
            !double.TryParse(builder.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return false;
        }

        if (negative)
        {
            number = -Math.Abs(number);
        }

        if (percent && percentAsFraction)
        {
            number /= 100d;
        }

        return true;
    }

    /// <summary>
    /// Decides whether a column reads as numbers, which is what right-aligns it.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <param name="columnIndex">The column.</param>
    /// <returns><see langword="true"/> when every populated cell holds a number.</returns>
    public static bool IsNumericColumn(IReadOnlyList<IReadOnlyList<string>> rows, int columnIndex)
    {
        var sawNumber = false;

        foreach (var row in rows)
        {
            if (row is null || columnIndex >= row.Count || string.IsNullOrWhiteSpace(row[columnIndex]))
            {
                continue;
            }

            if (!TryParseNumber(row[columnIndex], percentAsFraction: false, out _))
            {
                return false;
            }

            sawNumber = true;
        }

        return sawNumber;
    }

    /// <summary>
    /// Computes an aggregate over a column.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <param name="columnIndex">The column.</param>
    /// <param name="function">The function: <c>sum</c>, <c>average</c>, <c>count</c>, <c>min</c> or <c>max</c>.</param>
    /// <param name="percentAsFraction">Whether percentages are read as fractions.</param>
    /// <returns>The result, or <see langword="null"/> when there was nothing to aggregate.</returns>
    public static double? Aggregate(
        IReadOnlyList<IReadOnlyList<string>> rows,
        int columnIndex,
        string function,
        bool percentAsFraction)
    {
        var values = new List<double>();
        var populated = 0;

        foreach (var row in rows)
        {
            if (row is null || columnIndex >= row.Count || string.IsNullOrWhiteSpace(row[columnIndex]))
            {
                continue;
            }

            populated++;

            if (TryParseNumber(row[columnIndex], percentAsFraction, out var number))
            {
                values.Add(number);
            }
        }

        var name = function?.Trim().ToLowerInvariant();

        if (name is "count")
        {
            return populated;
        }

        if (values.Count == 0)
        {
            return null;
        }

        return name switch
        {
            "average" or "avg" or "mean" => values.Average(),
            "min" or "minimum" => values.Min(),
            "max" or "maximum" => values.Max(),
            _ => values.Sum(),
        };
    }

    /// <summary>
    /// Writes a computed number the way a cell holds it.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The invariant text.</returns>
    public static string ToInvariant(double value)
    {
        return Math.Round(value, 10).ToString("R", CultureInfo.InvariantCulture);
    }
}
