using System.Globalization;
using Cysharp.Text;

namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Pure helpers for inspecting a tabular query result. These exist so the model never has to add up
/// returned rows itself: a hand-summed column is a silent source of wrong answers, because nothing in
/// the pipeline can tell a mistyped total from a real one.
/// </summary>
internal static class TabularResultAnalyzer
{
    /// <summary>
    /// Converts a cell value to a number. A SQL <c>NULL</c> or blank cell counts as zero so a column
    /// stays summable, matching how SQL <c>SUM</c> skips nulls.
    /// </summary>
    /// <param name="value">The cell value.</param>
    /// <param name="number">The converted number.</param>
    /// <returns><see langword="true"/> when the value is numeric or empty.</returns>
    public static bool TryGetNumber(object value, out double number)
    {
        switch (value)
        {
            case null:
                number = 0;

                return true;

            case double doubleValue:
                number = doubleValue;

                return true;

            case long longValue:
                number = longValue;

                return true;

            case decimal decimalValue:
                number = (double)decimalValue;

                return true;

            case string text:
                if (string.IsNullOrWhiteSpace(text))
                {
                    number = 0;

                    return true;
                }

                return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out number);
            case IConvertible convertible:
                try
                {
                    number = convertible.ToDouble(CultureInfo.InvariantCulture);

                    return true;
                }
                catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
                {
                    number = 0;

                    return false;
                }

            default:
                number = 0;

                return false;
        }
    }

    /// <summary>
    /// Formats a number for display in a tool result.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <returns>The formatted number.</returns>
    public static string FormatNumber(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Determines whether a result is a key/measure pair: exactly two columns where every value in the
    /// second column is numeric. This is the shape the comparison tool joins on.
    /// </summary>
    /// <param name="result">The query result.</param>
    /// <returns><see langword="true"/> when the result is a key/measure pair.</returns>
    public static bool IsKeyMeasureShape(TabularQueryResult result)
    {
        if (result is null || result.Columns.Count != 2 || result.Rows.Count == 0)
        {
            return false;
        }

        var sawNumber = false;

        foreach (var row in result.Rows)
        {
            if (row.Length < 2 || !TryGetNumber(row[1], out _))
            {
                return false;
            }

            sawNumber |= row[1] is not null;
        }

        return sawNumber;
    }

    /// <summary>
    /// Builds a totals line covering every fully numeric column, so a multi-row result carries its own
    /// arithmetic instead of inviting the caller to add the rows up by hand.
    /// </summary>
    /// <param name="result">The query result.</param>
    /// <returns>The totals line, or <see langword="null"/> when no column qualifies.</returns>
    public static string FormatColumnTotals(TabularQueryResult result)
    {
        if (result is null || result.Rows.Count < 2 || result.Columns.Count == 0)
        {
            return null;
        }

        var totals = new double[result.Columns.Count];
        var summable = new bool[result.Columns.Count];

        for (var column = 0; column < result.Columns.Count; column++)
        {
            var total = 0d;
            var sawNumber = false;
            var numeric = true;

            foreach (var row in result.Rows)
            {
                if (column >= row.Length)
                {
                    numeric = false;

                    break;
                }

                if (!TryGetNumber(row[column], out var value))
                {
                    numeric = false;

                    break;
                }

                total += value;
                sawNumber |= row[column] is not null;
            }

            // A column of blanks parses as numeric but has nothing worth totalling.
            summable[column] = numeric && sawNumber;
            totals[column] = total;
        }

        if (!summable.Any(value => value))
        {
            return null;
        }

        using var builder = ZString.CreateStringBuilder();

        builder.Append("Column totals over the ");
        builder.Append(result.Rows.Count);
        builder.Append(" returned row(s)");

        if (result.Truncated)
        {
            builder.Append(", which are only the rows shown above and not the whole table");
        }

        builder.Append(": ");

        var first = true;

        for (var column = 0; column < result.Columns.Count; column++)
        {
            if (!summable[column])
            {
                continue;
            }

            if (!first)
            {
                builder.Append(" | ");
            }

            builder.Append(result.Columns[column]);
            builder.Append(" = ");
            builder.Append(FormatNumber(totals[column]));
            first = false;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Finds which loaded tables a query references. Table names are sanitized to letters, digits, and
    /// underscores when the workspace is built, so a word-boundary scan identifies them without needing
    /// a SQL parser.
    /// </summary>
    /// <param name="sql">The query text.</param>
    /// <param name="tables">The tables loaded in the workspace.</param>
    /// <returns>The referenced tables.</returns>
    public static IReadOnlyList<TabularTableInfo> FindReferencedTables(string sql, IReadOnlyList<TabularTableInfo> tables)
    {
        if (string.IsNullOrWhiteSpace(sql) || tables is not { Count: > 0 })
        {
            return [];
        }

        var referenced = new List<TabularTableInfo>();

        foreach (var table in tables)
        {
            if (!string.IsNullOrEmpty(table.TableName) && ContainsWord(sql, table.TableName))
            {
                referenced.Add(table);
            }
        }

        return referenced;
    }

    private static bool ContainsWord(string text, string word)
    {
        var index = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);

        while (index >= 0)
        {
            var beforeIsBoundary = index == 0 || !IsIdentifierCharacter(text[index - 1]);
            var afterIndex = index + word.Length;
            var afterIsBoundary = afterIndex >= text.Length || !IsIdentifierCharacter(text[afterIndex]);

            if (beforeIsBoundary && afterIsBoundary)
            {
                return true;
            }

            index = text.IndexOf(word, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsIdentifierCharacter(char value)
    {
        return char.IsLetterOrDigit(value) || value == '_';
    }
}
