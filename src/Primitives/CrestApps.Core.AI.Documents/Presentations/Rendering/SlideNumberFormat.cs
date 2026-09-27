using System.Globalization;
using System.Text;

namespace CrestApps.Core.AI.Documents.Presentations.Rendering;

/// <summary>
/// Formats chart values with the common parts of an Excel number format: decimals, thousands separators,
/// percentages, currency symbols and scaling commas.
/// </summary>
internal static class SlideNumberFormat
{
    /// <summary>
    /// Formats a value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="format">The number format code, or <see langword="null"/> for General.</param>
    /// <returns>The text drawn for the value.</returns>
    public static string Format(double value, string format)
    {
        if (string.IsNullOrWhiteSpace(format) || format.Equals("General", StringComparison.OrdinalIgnoreCase))
        {
            return General(value);
        }

        // Only the positive section of a multi-section format is used; negatives get a leading minus.
        var section = format.Split(';')[0];
        var cleaned = new StringBuilder();
        var inQuotes = false;
        var prefix = new StringBuilder();
        var suffix = new StringBuilder();
        var seenDigit = false;

        for (var index = 0; index < section.Length; index++)
        {
            var character = section[index];

            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (character == '[')
            {
                // Locale and colour tags such as [$€-2] or [Red]: keep a currency symbol, drop the rest.
                var end = section.IndexOf(']', index);
                var tag = end > index ? section[(index + 1)..end] : string.Empty;

                if (tag.StartsWith('$'))
                {
                    var symbol = tag[1..].Split('-')[0];
                    (seenDigit ? suffix : prefix).Append(symbol);
                }

                index = end > index ? end : section.Length;
                continue;
            }

            if (!inQuotes && character is '0' or '#' or '.' or ',' or '?')
            {
                seenDigit = true;
                cleaned.Append(character);
                continue;
            }

            if (character is '\\' or '_' or '*')
            {
                index++;
                continue;
            }

            (seenDigit ? suffix : prefix).Append(character);
        }

        var pattern = cleaned.ToString();
        var percent = suffix.ToString().Contains('%') || prefix.ToString().Contains('%');
        var scaled = percent ? value * 100 : value;

        // Trailing commas each divide by a thousand, as in 0.0,, for millions.
        while (pattern.EndsWith(','))
        {
            scaled /= 1000;
            pattern = pattern[..^1];
        }

        var decimals = pattern.Contains('.') ? pattern[(pattern.IndexOf('.') + 1)..].Count(character => character is '0' or '#' or '?') : 0;
        var grouping = pattern.Contains(',');
        var number = Math.Abs(scaled).ToString((grouping ? "#,##0" : "0") + (decimals > 0 ? "." + new string('0', decimals) : string.Empty), CultureInfo.InvariantCulture);
        var sign = scaled < 0 ? "-" : string.Empty;

        return sign + prefix + number + suffix;
    }

    /// <summary>
    /// Formats a value the way Excel's General format does, with at most a handful of significant decimals.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The text.</returns>
    public static string General(double value)
    {
        if (Math.Abs(value) >= 1e11 || (Math.Abs(value) < 1e-4 && value != 0))
        {
            return value.ToString("0.##E+0", CultureInfo.InvariantCulture);
        }

        return Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);
    }
}
