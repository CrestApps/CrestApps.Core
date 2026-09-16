using System.Globalization;

namespace CrestApps.Core.AI.Documents.Generation.Spreadsheets;

/// <summary>
/// Parses cell values into the numeric and date representations a spreadsheet stores natively.
/// <para>
/// Two levels of strictness are used deliberately. <see cref="TryParseNumber"/> is lenient because the
/// caller has already declared the column numeric, so a currency symbol or percent sign is noise to be
/// stripped. <see cref="LooksNumeric"/> is strict because it is used to guess at an undeclared column,
/// where wrongly converting an identifier such as a zip code would silently destroy data.
/// </para>
/// </summary>
public static class SpreadsheetValue
{
    // Values beyond this many digits lose precision when stored as a double, so they are kept as text.
    private const int MaximumSignificantDigits = 15;

    private static readonly string[] _dateFormats =
    [
        "yyyy-MM-dd",
        "yyyy/MM/dd",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-ddTHH:mm",
        "yyyy-MM-dd HH:mm",
    ];

    /// <summary>
    /// Parses a value the caller has already decided is numeric, stripping the presentation characters
    /// a source file may carry: currency symbols, thousands separators, a trailing percent sign, and
    /// accounting-style parentheses for negatives.
    /// </summary>
    /// <param name="value">The raw cell value.</param>
    /// <param name="number">The parsed number.</param>
    /// <returns><see langword="true"/> when the value parsed.</returns>
    public static bool TryParseNumber(string value, out double number)
    {
        number = 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        var isNegative = false;

        if (text.Length > 2 && text[0] == '(' && text[^1] == ')')
        {
            isNegative = true;
            text = text[1..^1].Trim();
        }

        var isPercent = false;

        if (text.EndsWith('%'))
        {
            isPercent = true;
            text = text[..^1].Trim();
        }

        Span<char> buffer = text.Length <= 64
            ? stackalloc char[text.Length]
            : new char[text.Length];

        var length = 0;

        foreach (var character in text)
        {
            if (char.IsDigit(character) || character is '.' or '-' or '+' or 'e' or 'E')
            {
                buffer[length++] = character;

                continue;
            }

            // Thousands separators, currency symbols, and whitespace are presentation, not value.
            if (character is ',' or ' ' or ' ' || char.IsSymbol(character) || char.IsPunctuation(character))
            {
                continue;
            }

            return false;
        }

        if (length == 0)
        {
            return false;
        }

        if (!double.TryParse(buffer[..length], NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return false;
        }

        if (isPercent)
        {
            // Percent-formatted cells store the fraction, so a source value of "15%" is stored as 0.15.
            number /= 100;
        }

        if (isNegative)
        {
            number = -number;
        }

        return true;
    }

    /// <summary>
    /// Determines whether a value is unambiguously a plain number, so a column with no declared format
    /// can be stored numerically without risking an identifier being mangled.
    /// <para>
    /// A value with a leading zero (<c>01234</c>), more digits than a double can hold exactly, or any
    /// currency or percent decoration is rejected: those are either identifiers or values whose
    /// presentation would be lost by silently converting them.
    /// </para>
    /// </summary>
    /// <param name="value">The raw cell value.</param>
    /// <param name="number">The parsed number.</param>
    /// <returns><see langword="true"/> when the value is a plain number.</returns>
    public static bool LooksNumeric(string value, out double number)
    {
        number = 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        var start = text[0] == '-' ? 1 : 0;

        if (start >= text.Length)
        {
            return false;
        }

        var digits = 0;
        var separators = 0;
        var decimalPoints = 0;
        var leadingDigits = 0;

        for (var index = start; index < text.Length; index++)
        {
            var character = text[index];

            if (char.IsAsciiDigit(character))
            {
                digits++;

                if (decimalPoints == 0)
                {
                    leadingDigits++;
                }

                continue;
            }

            if (character == ',')
            {
                // A separator after the decimal point is not a thousands separator.
                if (decimalPoints > 0)
                {
                    return false;
                }

                separators++;

                continue;
            }

            if (character == '.')
            {
                if (++decimalPoints > 1)
                {
                    return false;
                }

                continue;
            }

            return false;
        }

        if (digits == 0 || digits > MaximumSignificantDigits)
        {
            return false;
        }

        // A preserved leading zero marks an identifier (zip code, account number), not a quantity.
        if (leadingDigits > 1 && text[start] == '0')
        {
            return false;
        }

        var normalized = separators > 0
            ? text.Replace(",", string.Empty, StringComparison.Ordinal)
            : text;

        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    /// <summary>
    /// Parses a date value into the serial number a spreadsheet stores for dates.
    /// </summary>
    /// <param name="value">The raw cell value.</param>
    /// <param name="serial">The parsed date serial.</param>
    /// <returns><see langword="true"/> when the value parsed as a date.</returns>
    public static bool TryParseDate(string value, out double serial)
    {
        serial = 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();

        if (!DateTime.TryParseExact(text, _dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) &&
            !DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
        {
            return false;
        }

        return TryToSerial(parsed, out serial);
    }

    /// <summary>
    /// Determines whether a value is unambiguously a date, used to type a column with no declared
    /// format. Only the ISO-style shapes the tabular workspace normalizes dates into are accepted, so a
    /// value such as <c>1/2/2026</c> is left as text rather than guessed at with the wrong day order.
    /// </summary>
    /// <param name="value">The raw cell value.</param>
    /// <param name="serial">The parsed date serial.</param>
    /// <param name="hasTime">Whether the value carries a time component.</param>
    /// <returns><see langword="true"/> when the value is an unambiguous date.</returns>
    public static bool LooksLikeDate(string value, out double serial, out bool hasTime)
    {
        serial = 0;
        hasTime = false;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();

        if (text.Length < 8 || !DateTime.TryParseExact(
            text,
            _dateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed))
        {
            return false;
        }

        hasTime = parsed.TimeOfDay != TimeSpan.Zero || text.Length > 10;

        return TryToSerial(parsed, out serial);
    }

    private static bool TryToSerial(DateTime value, out double serial)
    {
        serial = 0;

        // The spreadsheet date system starts at 1900-01-01 and cannot represent anything before it.
        if (value.Year < 1900)
        {
            return false;
        }

        serial = value.ToOADate();

        return true;
    }
}
