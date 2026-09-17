using System.Globalization;
using System.Text;

namespace CrestApps.Core.AI.Documents.Generation.Spreadsheets;

/// <summary>
/// Turns a <see cref="SpreadsheetColumnFormat"/> into the number format code a spreadsheet application
/// understands.
/// </summary>
public static class SpreadsheetNumberFormatCode
{
    private const string DefaultCurrencySymbol = "$";

    /// <summary>
    /// Resolves the number format code for a column.
    /// </summary>
    /// <param name="format">The column format. May be <see langword="null"/>.</param>
    /// <returns>The format code, or <see langword="null"/> when the column needs no explicit format.</returns>
    public static string Resolve(SpreadsheetColumnFormat format)
    {
        if (format is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(format.FormatCode))
        {
            return format.FormatCode.Trim();
        }

        return format.NumberFormat switch
        {
            SpreadsheetNumberFormat.Text => "@",
            SpreadsheetNumberFormat.Number => BuildNumber(format),
            SpreadsheetNumberFormat.Currency => BuildCurrency(format),
            SpreadsheetNumberFormat.Accounting => BuildAccounting(format),
            SpreadsheetNumberFormat.Percent => BuildPercent(format),
            SpreadsheetNumberFormat.Scientific => "0.00E+00",
            SpreadsheetNumberFormat.Date => "yyyy\\-mm\\-dd",
            SpreadsheetNumberFormat.DateTime => "yyyy\\-mm\\-dd hh:mm",
            SpreadsheetNumberFormat.Time => "hh:mm:ss",
            SpreadsheetNumberFormat.Duration => "[h]:mm",
            _ => null,
        };
    }

    /// <summary>
    /// Determines whether a column's values should be stored as numbers rather than as text, based on
    /// its declared storage kind and number format.
    /// </summary>
    /// <param name="format">The column format. May be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the column is explicitly numeric.</returns>
    public static bool IsNumeric(SpreadsheetColumnFormat format)
    {
        if (format is null)
        {
            return false;
        }

        if (format.DataKind == SpreadsheetDataKind.Number)
        {
            return true;
        }

        if (format.DataKind != SpreadsheetDataKind.Auto)
        {
            return false;
        }

        return format.NumberFormat is
            SpreadsheetNumberFormat.Number or
            SpreadsheetNumberFormat.Currency or
            SpreadsheetNumberFormat.Accounting or
            SpreadsheetNumberFormat.Percent or
            SpreadsheetNumberFormat.Scientific;
    }

    /// <summary>
    /// Determines whether a column's values should be stored as dates.
    /// </summary>
    /// <param name="format">The column format. May be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the column is explicitly date-valued.</returns>
    public static bool IsDate(SpreadsheetColumnFormat format)
    {
        if (format is null)
        {
            return false;
        }

        if (format.DataKind == SpreadsheetDataKind.Date)
        {
            return true;
        }

        if (format.DataKind != SpreadsheetDataKind.Auto)
        {
            return false;
        }

        return format.NumberFormat is
            SpreadsheetNumberFormat.Date or
            SpreadsheetNumberFormat.DateTime or
            SpreadsheetNumberFormat.Time;
    }

    private static string BuildNumber(SpreadsheetColumnFormat format)
    {
        var body = "#,##0" + Decimals(format, defaultDecimals: 0);

        return format.NegativesInRed
            ? $"{body}_);[Red]({body})"
            : body;
    }

    private static string BuildCurrency(SpreadsheetColumnFormat format)
    {
        var symbol = Symbol(format);
        var body = $"{symbol}#,##0{Decimals(format, defaultDecimals: 2)}";

        return format.NegativesInRed
            ? $"{body}_);[Red]({body})"
            : $"{body}_);({body})";
    }

    private static string BuildAccounting(SpreadsheetColumnFormat format)
    {
        var symbol = Symbol(format);
        var decimals = Decimals(format, defaultDecimals: 2);
        var placeholders = new string('?', format.Decimals ?? 2);

        return $"_({symbol}* #,##0{decimals}_);_({symbol}* \\(#,##0{decimals}\\);_({symbol}* \"-\"{placeholders}_);_(@_)";
    }

    private static string BuildPercent(SpreadsheetColumnFormat format)
    {
        var body = "0" + Decimals(format, defaultDecimals: 0) + "%";

        return format.NegativesInRed
            ? $"{body};[Red]-{body}"
            : body;
    }

    private static string Decimals(SpreadsheetColumnFormat format, int defaultDecimals)
    {
        var decimals = format.Decimals ?? defaultDecimals;

        if (decimals <= 0)
        {
            return string.Empty;
        }

        // Excel tops out well below this, and an unbounded value from a model would produce a format
        // code the spreadsheet application rejects, corrupting the whole workbook.
        decimals = Math.Min(decimals, 10);

        return "." + new string('0', decimals);
    }

    private static string Symbol(SpreadsheetColumnFormat format)
    {
        var symbol = string.IsNullOrWhiteSpace(format.CurrencySymbol)
            ? DefaultCurrencySymbol
            : format.CurrencySymbol.Trim();

        var builder = new StringBuilder(symbol.Length + 2);
        builder.Append('"');

        foreach (var character in symbol)
        {
            // A quote inside the literal would terminate it early and invalidate the format code.
            if (character != '"')
            {
                builder.Append(character);
            }
        }

        builder.Append('"');

        return builder.ToString();
    }

    /// <summary>
    /// Formats a number the way the invariant culture writes it, so the value stored in the file is
    /// always machine-readable regardless of the server's locale.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <returns>The invariant representation.</returns>
    public static string ToInvariant(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }
}
