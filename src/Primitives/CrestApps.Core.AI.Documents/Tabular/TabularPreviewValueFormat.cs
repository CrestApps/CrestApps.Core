using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Generation.Spreadsheets;

namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Presents a cell value the way the exported workbook presents it.
/// </summary>
/// <remarks>
/// A workbook stores a number and a format code and leaves the presenting to whatever opens it, so a
/// preview drawn from the stored numbers shows <c>74612.15999999999</c> where the reader's spreadsheet
/// shows <c>$74,612.16</c> — the same figure, but not recognisably the same document. The format code is
/// therefore applied here, against the same specification the export writes into the file.
/// <para>
/// This reads the format code rather than the semantic description because a column that took its format
/// from the uploaded file carries only the code. What is covered is the vocabulary those files actually
/// use — a currency or accounting amount, a plain number, a percentage, a date — and a code outside it
/// falls back to the general presentation rather than being guessed at.
/// </para>
/// </remarks>
internal static class TabularPreviewValueFormat
{
    /// <summary>
    /// Presents a value using a column's recorded format.
    /// </summary>
    /// <param name="value">The stored value.</param>
    /// <param name="format">The column's format, or <see langword="null"/> when it has none.</param>
    /// <returns>The text to draw in the cell.</returns>
    public static string Format(object value, SpreadsheetColumnFormat format)
    {
        if (value is null or DBNull)
        {
            return string.Empty;
        }

        if (format is null)
        {
            return General(value);
        }

        var code = SpreadsheetNumberFormatCode.Resolve(format);

        if (string.IsNullOrWhiteSpace(code))
        {
            return General(value);
        }

        if (TryAsDateTime(value, out var date) && LooksLikeDate(code))
        {
            return FormatDate(date, code);
        }

        if (TryAsNumber(value, out var number))
        {
            return FormatNumber(number, code);
        }

        return General(value);
    }

    /// <summary>
    /// Presents a value that has no recorded format.
    /// </summary>
    /// <param name="value">The stored value.</param>
    /// <returns>The text to draw in the cell.</returns>
    /// <remarks>
    /// A spreadsheet's general presentation never prints a number to full binary precision; it shows the
    /// significant digits and leaves the rest. Printing the stored double verbatim is what puts
    /// <c>74612.15999999999</c> and <c>184363.92000000004</c> in a cell the file shows as a plain amount,
    /// so the general path rounds rather than round-trips.
    /// </remarks>
    public static string General(object value)
    {
        return value switch
        {
            null or DBNull => string.Empty,
            string text => text,
            bool flag => flag ? "TRUE" : "FALSE",
            double number => GeneralNumber(number),
            float number => GeneralNumber(number),
            decimal number => GeneralNumber((double)number),
            long number => number.ToString(CultureInfo.InvariantCulture),
            int number => number.ToString(CultureInfo.InvariantCulture),
            DateTime date => date.TimeOfDay == TimeSpan.Zero
                ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            byte[] bytes => $"({bytes.Length.ToString("N0", CultureInfo.InvariantCulture)} bytes)",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    private static string GeneralNumber(double number)
    {
        if (double.IsNaN(number) || double.IsInfinity(number))
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        // Eleven significant digits is what a spreadsheet's general presentation shows, and it is also
        // what turns the accumulated binary error at the tail of a sum back into the figure that was
        // summed. "G11" then drops a trailing run of zeros that rounding leaves behind.
        var rounded = number.ToString("G11", CultureInfo.InvariantCulture);

        return rounded.Contains('E', StringComparison.OrdinalIgnoreCase)
            ? number.ToString(CultureInfo.InvariantCulture)
            : rounded;
    }

    private static string FormatNumber(double number, string code)
    {
        var section = NegativeSection(code, number);
        var isPercent = section.Contains('%', StringComparison.Ordinal);

        if (isPercent)
        {
            number *= 100d;
        }

        var decimals = DecimalPlaces(section);
        var grouped = section.Contains("#,#", StringComparison.Ordinal) || section.Contains("0,0", StringComparison.Ordinal);
        var magnitude = Math.Abs(number);

        var text = magnitude.ToString(
            (grouped ? "#,##0" : "0") + (decimals > 0 ? "." + new string('0', decimals) : string.Empty),
            CultureInfo.InvariantCulture);

        var symbol = CurrencySymbol(section);

        if (!string.IsNullOrEmpty(symbol))
        {
            text = symbol + text;
        }

        if (isPercent)
        {
            text += "%";
        }

        // An accounting code writes its negatives in brackets and says so in its own second section; any
        // other code carries the sign. Zero is never signed.
        if (number < 0 && magnitude > 0)
        {
            text = ParenthesizesNegatives(code)
                ? "(" + text + ")"
                : "-" + text;
        }

        return text;
    }

    private static string FormatDate(DateTime date, string code)
    {
        // The codes a spreadsheet writes use the same letters .NET does, with one collision: in a format
        // code "m" after an hour marker is a minute and elsewhere a month, and a lone "d"/"m"/"y" run is
        // case-insensitive. Lower-cased month/day/year runs are mapped across; the rest is left alone.
        var builder = new StringBuilder(code.Length);
        var seenHour = false;

        for (var index = 0; index < code.Length; index++)
        {
            var character = code[index];

            switch (character)
            {
                case 'h':
                case 'H':
                    seenHour = true;
                    builder.Append('H');
                    break;
                case 'y':
                case 'Y':
                    builder.Append('y');
                    break;
                case 'd':
                case 'D':
                    builder.Append('d');
                    break;
                case 'm':
                case 'M':
                    builder.Append(seenHour ? 'm' : 'M');
                    break;
                case 's':
                case 'S':
                    builder.Append('s');
                    break;
                case '\\':
                    if (index + 1 < code.Length)
                    {
                        builder.Append(code[++index]);
                    }
                    break;
                case ';':
                    // Only the first section describes a date.
                    index = code.Length;
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        var pattern = builder.ToString().Trim();

        if (pattern.Length == 0)
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        try
        {
            return date.ToString(pattern, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Returns the part of a format code that applies to a value of this sign.
    /// </summary>
    private static string NegativeSection(string code, double number)
    {
        var sections = SplitSections(code);

        if (number < 0 && sections.Count > 1)
        {
            return sections[1];
        }

        return sections.Count > 0 ? sections[0] : code;
    }

    private static bool ParenthesizesNegatives(string code)
    {
        var sections = SplitSections(code);

        return sections.Count > 1 && sections[1].Contains('(', StringComparison.Ordinal);
    }

    /// <summary>
    /// Splits a format code on the semicolons that separate its positive, negative, zero and text parts,
    /// ignoring the ones inside a quoted literal or escaped.
    /// </summary>
    private static List<string> SplitSections(string code)
    {
        var sections = new List<string>(4);
        var builder = new StringBuilder(code.Length);
        var quoted = false;

        for (var index = 0; index < code.Length; index++)
        {
            var character = code[index];

            if (character == '"')
            {
                quoted = !quoted;
                builder.Append(character);
                continue;
            }

            if (character == '\\' && index + 1 < code.Length)
            {
                builder.Append(character).Append(code[++index]);
                continue;
            }

            if (character == ';' && !quoted)
            {
                sections.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            builder.Append(character);
        }

        sections.Add(builder.ToString());

        return sections;
    }

    /// <summary>
    /// Counts the digit placeholders after the decimal point, ignoring quoted literals.
    /// </summary>
    private static int DecimalPlaces(string section)
    {
        var separator = IndexOfUnquoted(section, '.');

        if (separator < 0)
        {
            return 0;
        }

        var decimals = 0;

        for (var index = separator + 1; index < section.Length; index++)
        {
            var character = section[index];

            if (character is '0' or '#' or '?')
            {
                decimals++;
                continue;
            }

            if (character == '"')
            {
                break;
            }
        }

        return decimals;
    }

    /// <summary>
    /// Reads the currency symbol a code prints ahead of its digits.
    /// </summary>
    /// <remarks>
    /// A symbol reaches a code two ways: quoted, as <c>"$"#,##0.00</c>, or escaped, as <c>\$#,##0</c>.
    /// Only a symbol standing before the first digit placeholder is taken, so the <c>"-"</c> a zero
    /// section prints and the <c>_(</c> padding of an accounting code are not mistaken for one.
    /// </remarks>
    private static string CurrencySymbol(string section)
    {
        var symbol = new StringBuilder(4);

        for (var index = 0; index < section.Length; index++)
        {
            var character = section[index];

            if (character is '0' or '#' or '?')
            {
                break;
            }

            if (character == '"')
            {
                var close = section.IndexOf('"', index + 1);

                if (close < 0)
                {
                    break;
                }

                var literal = section[(index + 1)..close];

                // A zero section prints a dash in place of the digits; that is not a currency symbol.
                if (literal != "-")
                {
                    symbol.Append(literal);
                }

                index = close;
                continue;
            }

            // Only an escaped currency character counts. An accounting code escapes the bracket it wraps
            // negatives in as "\(", and taking that as a symbol prints the bracket twice.
            if (character == '\\' && index + 1 < section.Length)
            {
                var escaped = section[++index];

                if (IsCurrency(escaped))
                {
                    symbol.Append(escaped);
                }

                continue;
            }

            if (IsCurrency(character))
            {
                symbol.Append(character);
            }
        }

        return symbol.ToString();
    }

    private static bool IsCurrency(char character)
    {
        return character is '$' or '£' or '€' or '¥';
    }

    private static int IndexOfUnquoted(string value, char target)
    {
        var quoted = false;

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];

            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (character == '\\')
            {
                index++;
                continue;
            }

            if (character == target && !quoted)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool LooksLikeDate(string code)
    {
        var section = SplitSections(code)[0];
        var quoted = false;

        foreach (var character in section)
        {
            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (!quoted && character is 'y' or 'Y' or 'd' or 'D' or 'h' or 'H' or 's' or 'S')
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryAsNumber(object value, out double number)
    {
        switch (value)
        {
            case double typed:
                number = typed;
                return true;
            case float typed:
                number = typed;
                return true;
            case decimal typed:
                number = (double)typed;
                return true;
            case long typed:
                number = typed;
                return true;
            case int typed:
                number = typed;
                return true;
            case short typed:
                number = typed;
                return true;
            case string text when double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed):
                number = parsed;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    private static bool TryAsDateTime(object value, out DateTime date)
    {
        switch (value)
        {
            case DateTime typed:
                date = typed;
                return true;
            case DateTimeOffset typed:
                date = typed.DateTime;
                return true;
            case string text when DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed):
                date = parsed;
                return true;
            default:
                date = default;
                return false;
        }
    }
}
