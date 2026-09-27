using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CrestApps.Core.AI.Documents.Pdf.Analysis;

namespace CrestApps.Core.AI.Documents.Pdf.Intelligence;

/// <summary>
/// Finds the fixed-shape values of a text with <see cref="PdfPatternLibrary"/>, and normalizes them so the
/// same amount, date or number written two ways compares equal.
/// </summary>
internal static partial class PdfValues
{
    private static readonly string[] _dateFormats =
    [
        "yyyy-MM-dd",
        "M/d/yyyy",
        "M/d/yy",
        "d.M.yyyy",
        "d-M-yyyy",
        "MMMM d, yyyy",
        "MMMM d yyyy",
        "MMM d, yyyy",
        "MMM d yyyy",
        "MMM. d, yyyy",
        "d MMMM yyyy",
        "d MMM yyyy",
        "d MMM. yyyy",
    ];

    /// <summary>
    /// Finds values of the given kinds.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="kinds">The kinds, as <see cref="PdfPatternLibrary"/> names them.</param>
    /// <returns>The values, in the order they appear.</returns>
    /// <remarks>
    /// A date written <c>2026-03-15</c> or <c>15.03.2026</c> also has the shape of a phone number, and the
    /// library settles a tie between two equally long matches by the order it looks for them, in which phone
    /// numbers come before dates. A date is never a phone number, so a phone match a date covers is dropped
    /// here, and the date kept when dates were asked for.
    /// </remarks>
    public static List<PdfPatternMatch> Find(string text, IReadOnlyCollection<string> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);

        var matches = PdfPatternLibrary.Find(text, kinds);

        if (!kinds.Contains(PdfPatternLibrary.Phone) || matches.Count == 0)
        {
            return matches;
        }

        var dates = PdfPatternLibrary.Find(text, [PdfPatternLibrary.Date]);

        if (dates.Count == 0)
        {
            return matches;
        }

        matches.RemoveAll(match => match.Kind == PdfPatternLibrary.Phone && dates.Exists(date => Overlaps(date, match)));

        if (kinds.Contains(PdfPatternLibrary.Date))
        {
            foreach (var date in dates)
            {
                if (!matches.Exists(match => Overlaps(date, match)))
                {
                    matches.Add(date);
                }
            }
        }

        return [.. matches.OrderBy(match => match.Index)];
    }

    /// <summary>
    /// Normalizes a value so the same amount, date or address written differently compares equal.
    /// </summary>
    /// <param name="kind">The value's kind, as <see cref="PdfPatternLibrary"/> names it.</param>
    /// <param name="value">The value as written.</param>
    /// <returns>The normalized value, or <see langword="null"/> when it cannot be read.</returns>
    public static string Normalize(string kind, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        switch (kind)
        {
            case PdfPatternLibrary.Money or PdfPatternLibrary.Percentage:
                var amount = ParseAmount(value);

                if (amount is null)
                {
                    return null;
                }

                var number = amount.Value.ToString("0.####", CultureInfo.InvariantCulture);

                return kind == PdfPatternLibrary.Percentage
                    ? number + "%"
                    : number;

            case PdfPatternLibrary.Date:
                var cleaned = OrdinalSuffix().Replace(PdfCorpus.CollapseWhitespace(value), "$1");

                return DateTime.TryParseExact(cleaned, _dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date)
                    ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : cleaned.ToLowerInvariant();

            case PdfPatternLibrary.Phone or PdfPatternLibrary.CreditCard or PdfPatternLibrary.UsSocialSecurityNumber:
                return new string([.. value.Where(char.IsAsciiDigit)]);

            case PdfPatternLibrary.Iban:
                return new string([.. value.Where(char.IsLetterOrDigit)]).ToUpperInvariant();

            case PdfPatternLibrary.Url:
                return value.Trim().TrimEnd('/').ToLowerInvariant();

            default:
                return PdfCorpus.CollapseWhitespace(value).ToLowerInvariant();
        }
    }

    /// <summary>
    /// Writes a value for display, masking the kinds that identify a person or an account.
    /// </summary>
    /// <param name="kind">The value's kind.</param>
    /// <param name="value">The value as written.</param>
    /// <returns>The value; a card number, IBAN or social security number masked.</returns>
    public static string Display(string kind, string value)
    {
        return kind is PdfPatternLibrary.CreditCard or PdfPatternLibrary.Iban or PdfPatternLibrary.UsSocialSecurityNumber
            ? PdfPatternLibrary.Mask(kind, value)
            : PdfCorpus.CollapseWhitespace(value);
    }

    /// <summary>
    /// Reads an amount, a percentage or a plain number, with a multiplier word such as <c>million</c>.
    /// </summary>
    /// <param name="value">The value as written, for example <c>$1.2 million</c> or <c>(1,250.00)</c>.</param>
    /// <returns>The number, or <see langword="null"/> when the text holds none.</returns>
    public static double? ParseAmount(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim().ToLowerInvariant();
        var multiplier = 1d;

        if (EndsWithUnit(text, "billion", 'b'))
        {
            multiplier = 1_000_000_000;
        }
        else if (EndsWithUnit(text, "million", 'm'))
        {
            multiplier = 1_000_000;
        }
        else if (EndsWithUnit(text, "thousand", 'k'))
        {
            multiplier = 1_000;
        }

        var digits = new StringBuilder();

        foreach (var character in text)
        {
            if (char.IsDigit(character) || character == '.' || (character == '-' && digits.Length == 0))
            {
                digits.Append(character);
            }
        }

        if (!double.TryParse(digits.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }

        // Accounts write a negative amount in parentheses.
        if (text.StartsWith('(') && text.EndsWith(')'))
        {
            number = -Math.Abs(number);
        }

        return number * multiplier;
    }

    private static bool EndsWithUnit(string text, string word, char letter)
    {
        if (text.EndsWith(word, StringComparison.Ordinal))
        {
            return true;
        }

        if (!text.EndsWith(letter))
        {
            return false;
        }

        // "5m" and "5 m" are five million; "5 cm" is not.
        var rest = text[..^1].TrimEnd();

        return rest.Length > 0 && char.IsDigit(rest[^1]);
    }

    private static bool Overlaps(PdfPatternMatch first, PdfPatternMatch second)
    {
        return first.Index < second.Index + second.Length && second.Index < first.Index + first.Length;
    }

    [GeneratedRegex(@"(\d)(?:st|nd|rd|th)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OrdinalSuffix();
}
